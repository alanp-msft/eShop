---
title: "Order Cancellation: Authorization and Event Delivery"
description: "How customer-initiated order cancellation is authorized and how OrderCancelled events and stock release are delivered reliably"
ms.date: 2026-09-15
status: proposed
---

## Context

The eShop Orders page (WebApp) will let a signed-in customer cancel their own order while it is still `Submitted` or `AwaitingValidation` (charter: `.copilot-tracking/sdlc/order-cancellation/charter.md`; requirements: `.copilot-tracking/sdlc/order-cancellation/requirements.md`). Three existing pieces of Ordering.API/Domain code are directly relevant:

* `CancelOrderCommandHandler` (`src/Ordering.API/Application/Commands/CancelOrderCommandHandler.cs`) loads the order by `OrderNumber` and calls `orderToUpdate.SetCancelledStatus()` with **no check that the caller is the order's owner**. The security plan (`.copilot-tracking/security-plans/order-cancellation/security-plan-order-cancellation.md`, finding **T-ORDERINGAPI-001**, Critical) confirms this is an IDOR / broken object-level authorization gap: any authenticated user can cancel any other buyer's order. This directly contradicts REQ-002 and is flagged as blocking for the design gate.
* `Order.SetCancelledStatus()` (`src/Ordering.Domain/AggregatesModel/OrderAggregate/Order.cs`) already enforces the state-machine guard (rejects `Paid`/`Shipped`) and raises `OrderCancelledDomainEvent`. This satisfies REQ-001/REQ-003 and is not in scope for this decision.
* `OrderCancelledDomainEventHandler` (`src/Ordering.API/Application/DomainEventHandlers/OrderCancelledDomainEventHandler.cs`) already converts the domain event into an `OrderStatusChangedToCancelledIntegrationEvent` and calls `IOrderingIntegrationEventService.AddAndSaveEventAsync(...)`, which persists the event through the `IntegrationEventLogEF` outbox (same `OrderingContext` transaction) rather than publishing directly to the bus. `OrderingIntegrationEventService.PublishEventsThroughEventBusAsync` later drains the outbox and marks entries in-progress/published/failed. This is the same pattern used for every other order-status transition (`Paid`, `Shipped`, `StockConfirmed`, `AwaitingValidation`).
* `OrdersApi.CancelOrderAsync` (`src/Ordering.API/Apis/OrdersApi.cs`) already wraps the command in `IdentifiedCommand<CancelOrderCommand, bool>` and requires an `x-requestid` header; `CancelOrderIdentifiedCommandHandler` (`IdentifiedCommandHandler<CancelOrderCommand, bool>`) short-circuits duplicate request IDs via the `IRequestManager`/`ClientRequest` table, returning `true` without re-invoking the domain logic. This request-id de-duplication mechanism is shared by every other identified command (`CreateOrderCommand`, `ShipOrderCommand`) and is the only idempotency mechanism currently wired into the pipeline.

This ADR records how we close the T-ORDERINGAPI-001 gap and confirms the event-delivery and idempotency mechanisms for the new cancellation path, so the design gate has a documented, reviewable decision rather than an implicit default.

**Assumption:** `{{tech-lead}}` and `{{code-owner}}` are the design-gate/PR-gate approvers per the charter; this ADR is written for their review and does not require synchronous coaching to produce a first draft, per the task's autonomous-execution instruction.

## Decision Drivers

* **REQ-002 (must):** only the owning buyer may cancel their order; non-owners get `403`, unauthenticated callers get `401`.
* **REQ-005 (must):** exactly one `OrderStatusChangedToCancelledIntegrationEvent` is published per successful cancellation, after the state change commits.
* **REQ-004 (must):** stock reservation release must follow from that event, so publish reliability directly gates inventory correctness.
* **REQ-007 (must):** repeated cancel requests (same or new `x-requestid`) must not re-execute cancellation logic or double-publish the event.
* **T-ORDERINGAPI-001 (Critical, security plan):** the fix must be structural (enforced in the handler/pipeline), not just a UI-layer hide-the-button mitigation, since the API is the actual trust boundary.
* **Consistency:** minimize divergence from the existing `Ship`/`Paid`/`StockConfirmed` handler and outbox patterns to keep the codebase's mental model uniform and reduce review risk for a medium-risk-tier change.
* **Blast radius:** the fix should not require reworking the domain aggregate, the outbox infrastructure, or the request-manager idempotency table, all of which are shared by other commands.

## Options Considered

### (a) Authorization: extend `CancelOrderCommand`/handler with a buyer check vs. a new customer-scoped command

**Option A1 — Add an ownership check inside the existing `CancelOrderCommandHandler`.** Inject `IIdentityService` (already used elsewhere, e.g. `OrdersApi.GetOrdersByUserAsync`), load the order, compare `order.BuyerId` (or the buyer's `IdentityGuid`) against `IdentityService.GetUserIdentity()`, and return a distinguishable "forbidden" result before calling `SetCancelledStatus()`.
* Trade-offs: smallest possible diff; reuses the existing `CancelOrderCommand`/`OrdersApi.CancelOrderAsync` wiring, the existing `IdentifiedCommand` idempotency wrapper, and the existing test surface. Requires changing `CancelOrderCommandHandler.Handle`'s return type (or adding an out-of-band result) from `bool` to something that can express "not found" vs. "forbidden" vs. "succeeded", which is a small breaking change to `IRequestHandler<CancelOrderCommand, bool>` and to `OrdersApi.CancelOrderAsync`'s `Results<...>` mapping.

**Option A2 — Introduce a new customer-scoped command (e.g. `CancelMyOrderCommand`) carrying the caller's identity as part of the command payload,** leaving `CancelOrderCommand` as an internal/admin-capable command for future support workflows.
* Trade-offs: cleaner separation of "who is allowed to invoke this" from the generic cancel operation, and anticipates a future admin-cancellation path (explicitly out of scope per the charter). Adds a second command/handler/route pair to maintain, a second set of idempotency wiring (`IdentifiedCommand<CancelMyOrderCommand, ...>`), and duplicates guard logic unless it delegates to the same domain method — more surface area for a feature whose charter explicitly excludes admin cancellation.

**Option A3 — Enforce ownership in a MediatR pipeline behavior** (e.g. an `IPipelineBehavior<CancelOrderCommand, bool>`) instead of inline in the handler.
* Trade-offs: reusable if more owner-scoped commands appear later; keeps the handler itself focused on domain orchestration. Adds an indirection layer and a new pipeline registration for a single call site today, which is more infrastructure than the current single-command need justifies.

### (b) Event delivery: `IntegrationEventLogEF` outbox vs. direct publish

**Option B1 — Continue routing `OrderStatusChangedToCancelledIntegrationEvent` through the existing outbox** (`OrderCancelledDomainEventHandler` → `IOrderingIntegrationEventService.AddAndSaveEventAsync` → same DB transaction as the order-status change → `PublishEventsThroughEventBusAsync` drains and publishes, marking `InProgress`/`Published`/`Failed`).
* Trade-offs: this is already implemented and already used for every other status transition; the event row commits atomically with `OrderStatus = Cancelled`, so a crash between state change and publish cannot silently lose the event — it remains `Published = false` in the log for retry/inspection. Publish latency is not synchronous with the API response (eventual, matching REQ-005's "after `SaveEntitiesAsync` commits" wording), and failures currently rely on `MarkEventAsFailedAsync` plus the event-bus's own retry topology rather than an explicit re-drive job — a gap already tracked as T-EVENTBUS-003 in the security plan, not something newly introduced by this decision.

**Option B2 — Publish `OrderCancelled` directly to the bus from the domain event handler,** bypassing the outbox.
* Trade-offs: marginally simpler call chain, but breaks atomicity with the order-status commit (a publish that succeeds before a DB commit failure, or vice versa, produces a state/event mismatch) and diverges from every other status-change handler in the same file family, increasing review and maintenance cost for no measurable benefit given REQ-004/REQ-005's reliability requirements.

### (c) Idempotency: existing `IdentifiedCommand` request-id pattern vs. status-based no-op

**Option C1 — Keep the existing `IdentifiedCommand<CancelOrderCommand, bool>` / `CancelOrderIdentifiedCommandHandler` request-id de-duplication,** unchanged, for the cancellation path.
* Trade-offs: already implemented, already required by `OrdersApi.CancelOrderAsync` (400 on missing `x-requestid`), and already covered by `IRequestManager`'s `ClientRequest` table. Handles the "identical retry with the same request ID" case (REQ-007's first scenario) with zero new code. Does **not** by itself handle "a new `x-requestid` sent against an order that is already `Cancelled`" (REQ-007's second scenario) — that case falls through to the handler and depends on domain/handler behavior, not the request-id table.
* Note (T-ORDERINGDOMAIN-003, security plan): the request-id table only dedupes identical IDs; two different `x-requestid`s racing on the same order can both pass `SetCancelledStatus()`'s guard before either commits. This is a pre-existing concurrency gap in the shared `IdentifiedCommand` pattern, not unique to cancellation, and is called out as a residual risk rather than solved here.

**Option C2 — Add a status-based no-op guard:** when the handler loads an order already in `OrderStatus.Cancelled`, short-circuit and return success without calling `SetCancelledStatus()` again (which would otherwise re-raise `OrderCancelledDomainEvent` and a duplicate integration event, since `SetCancelledStatus()` does not currently guard against being called when already `Cancelled` — unlike `SetAwaitingValidationStatus()`/`SetStockConfirmedStatus()`, which check the *current* status before transitioning).
* Trade-offs: directly closes REQ-007's second scenario (new request ID, already-cancelled order) and T-ORDERINGDOMAIN-003's double-publish risk, complementing rather than replacing C1. Adds one additional status check in the handler (or in `SetCancelledStatus()` itself) — a small, low-risk addition consistent with the pattern already used by other `Set*Status()` methods in `Order.cs`.

## Decision

1. **Authorization — Option A1.** Extend `CancelOrderCommandHandler` with an ownership check rather than introducing a new command. Inject `IIdentityService`, load the order, and compare the authenticated caller's identity against the order's owning buyer before invoking `SetCancelledStatus()`. Change the handler's result to distinguish not-found, forbidden, and success (e.g., a small result enum/record instead of a bare `bool`), and update `OrdersApi.CancelOrderAsync` to map these to `404`/`403`/`401`/`200` per REQ-002. This directly remediates T-ORDERINGAPI-001 with the smallest change that reuses the existing command, route, and idempotency wrapper. A2 and A3 are rejected as over-engineering for a single, in-scope, customer-only cancellation path; A2's admin-cancellation motivation is explicitly out of scope per the charter, and A3's reusable-pipeline motivation has no second consumer today.

2. **Event delivery — Option B1.** Keep publishing `OrderStatusChangedToCancelledIntegrationEvent` through the existing `IntegrationEventLogEF` outbox via `OrderCancelledDomainEventHandler` → `IOrderingIntegrationEventService.AddAndSaveEventAsync`, unchanged. No new publish path is introduced. B2 is rejected because it breaks the atomicity the outbox provides and diverges from the pattern used by every sibling status-change handler.

3. **Idempotency — Options C1 + C2 together.** Retain the existing `IdentifiedCommand`/request-id de-duplication (C1) as the first line of defense for literal retries, and add a status-based no-op guard (C2) so a cancel request against an already-`Cancelled` order returns success without re-raising `OrderCancelledDomainEvent` or re-publishing the integration event, regardless of `x-requestid`. This combination is required to satisfy both scenarios in REQ-007's acceptance criteria; C1 alone only covers the same-request-id case.

## Consequences

* `CancelOrderCommandHandler`, `CancelOrderCommand`/result type, and `OrdersApi.CancelOrderAsync`'s response mapping change together; existing callers of `CancelOrderCommandHandler.Handle` that assume a `bool` result need to be updated in the same change set.
* `Order.SetCancelledStatus()` (or the handler immediately before calling it) gains a no-op branch for the already-`Cancelled` case; unit tests must cover this alongside the existing `Paid`/`Shipped` guard tests.
* No changes are required to `OrderingIntegrationEventService`, `IntegrationEventLogEF`, or the RabbitMQ event bus — this decision explicitly avoids touching shared outbox/bus infrastructure.
* T-ORDERINGAPI-001 is resolved as a blocking design-gate item; T-ORDERINGDOMAIN-002 (event lacks explicit "initiated by" identity) and T-ORDERINGDOMAIN-003 (cross-request-id race) remain open as separate, lower-severity follow-ups tracked in the security plan and are not solved by this ADR.
* `{{tech-lead}}` should confirm the exact HTTP status mapping (`403` vs. a generic `401`/`400`) matches REQ-002's wording before implementation begins, and `{{code-owner}}` should confirm test coverage expectations for the new forbidden/no-op branches at PR gate.

## Requirements Addressed

* REQ-001 — Cancellation restricted to eligible order statuses (unaffected; confirmed already enforced by `Order.SetCancelledStatus()`).
* REQ-002 — Only the owning customer can cancel their order (primary driver for the A1 decision).
* REQ-003 — Domain enforces the `Cancelled` transition with guards (unaffected by this decision; extended slightly by the C2 no-op guard).
* REQ-004 — Reserved stock is released on cancellation (depends on reliable event delivery — B1 decision).
* REQ-005 — `OrderCancelled` integration event published for downstream consumers (B1 decision).
* REQ-007 — Repeated cancel requests are idempotent (C1 + C2 decision).
* REQ-008 — Audit of the acting customer's identity (supported indirectly: the A1 ownership check requires resolving and can log the caller's identity at cancellation time).

> AI-assisted content; review and validate before use.
