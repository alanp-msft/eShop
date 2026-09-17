<!-- markdownlint-disable-file -->
---
title: "Order Cancellation Implementation Plan"
description: "Task-centered implementation plan for customer-initiated order cancellation (project order-cancellation)"
ms.date: 2026-09-16
---

## User Requests

1. "implement customer-initiated order cancellation in eShop for project order-cancellation" — satisfy REQ-001 through REQ-009 in `.copilot-tracking/sdlc/order-cancellation/requirements.md`.
2. Follow the approved ADR `docs/decisions/2026-09-15-order-cancellation-authorization-and-events.md`: ownership check inside the existing `CancelOrderCommandHandler` (Option A1), returning a distinguishable not-found/forbidden/success (extended here with an ineligible-status case for REQ-001); publish `OrderStatusChangedToCancelledIntegrationEvent` through the existing `IntegrationEventLogEF` outbox (Option B1, unchanged); keep `IdentifiedCommand` request-id de-duplication and add a status-based no-op guard (Options C1+C2).
3. Address the security plan `.copilot-tracking/security-plans/order-cancellation/security-plan-order-cancellation.md`, specifically `T-ORDERINGAPI-001` (Critical — blocking) and the `SEC-TEMP-*` backlog items that map to in-scope requirements (`SEC-TEMP-1`, `SEC-TEMP-2`, `SEC-TEMP-3`, `SEC-TEMP-7`, `SEC-TEMP-9`, `SEC-TEMP-11`).
4. Follow `.github/instructions/hve4isd/dotnet-enterprise.instructions.md` traceability conventions: `[TestMethod("REQ-nnn ...")]` (MSTest projects) / `[Fact(DisplayName = "REQ-nnn ...")]` (existing xUnit `Ordering.FunctionalTests` project — see Assumption A2) and `// REQ-nnn` comments on implementing classes.
5. Follow `.github/instructions/hve4isd/sdlc-artifacts.instructions.md` for citing requirement identifiers in this plan.
6. Plan only — no changes under `src/` or `tests/` in this turn; this document and its companion critique are the only deliverables.

## Overview and Objectives

Close the confirmed IDOR gap in `CancelOrderCommandHandler` (T-ORDERINGAPI-001), make the eligible-status guard surface as a client error instead of a 500, keep the existing outbox-based event publish path unchanged, add the missing "already cancelled" idempotency guard, and extend the WebApp so a signed-in customer can trigger and see the result of a cancellation — all while adding the audit logging and telemetry the security plan and REQ-008/REQ-009 call for. Work is derived from reading the current implementation (see References in each task); no requirement is inferred beyond what `requirements.md` and the ADR state.

Derived objectives, with reasoning:

* **Make `CancelOrderCommandHandler` the single authorization and eligibility boundary** (not the API layer, not the WebApp) — this is what ADR Option A1 and `T-ORDERINGAPI-001` require, and it lets `OrdersApi.CancelOrderAsync` stay a thin mapper.
* **Replace the handler's `bool` return with a small result type** — required because `bool` cannot express not-found vs. forbidden vs. ineligible-status vs. success, and the ADR's "Consequences" section explicitly calls out that this is a breaking change to `IRequestHandler<CancelOrderCommand, bool>` that must land together with the `OrdersApi` mapping change.
* **Pre-check status eligibility in the handler rather than relying on the domain exception path** — reading `Ordering.API/Application/Commands/IdentifiedCommandHandler.cs` shows its `Handle` method has a bare `catch { return default; }` that swallows the `OrderingDomainException` thrown by `Order.SetCancelledStatus()` for `Paid`/`Shipped` orders and returns `default(bool)` (`false`), which `OrdersApi.CancelOrderAsync` currently maps to a `500 Problem`. REQ-001 requires this to be a client error, so the handler must detect the ineligible status itself and return a typed result before ever calling `SetCancelledStatus()` on a `Paid`/`Shipped` order.
* **Keep `Order.SetCancelledStatus()`'s own guard exception behavior unchanged** — REQ-001 explicitly requires it to keep throwing for `Paid`/`Shipped`; only add the already-`Cancelled` no-op branch that ADR Option C2 calls for.
* **No new Catalog.API stock-decrement/reservation model** — reading `Catalog.API/IntegrationEvents/EventHandling/OrderStatusChangedToPaidIntegrationEventHandler.cs` and `OrderStatusChangedToAwaitingValidationIntegrationEventHandler.cs` shows the current codebase only checks `AvailableStock` (does not decrement it) at `AwaitingValidation`, and only decrements it (`RemoveStock`) once an order reaches `Paid`. Since cancellation is restricted to `Submitted`/`AwaitingValidation` orders (REQ-001), there is never a stock reservation to release for any order this feature can legally cancel. See Assumption A1 for how REQ-004 is satisfied given this constraint.

## Context Summary

* **Requirements**: `.copilot-tracking/sdlc/order-cancellation/requirements.md` — REQ-001 through REQ-009 (all `must` except REQ-008/REQ-009 `should`).
* **ADR**: `docs/decisions/2026-09-15-order-cancellation-authorization-and-events.md` — decisions A1 (authorization in-handler), B1 (outbox unchanged), C1+C2 (idempotency).
* **Security plan**: `.copilot-tracking/security-plans/order-cancellation/security-plan-order-cancellation.md` — `T-ORDERINGAPI-001` (Critical, blocking design gate), `T-ORDERINGDOMAIN-002`/`T-ORDERINGDOMAIN-003` (explicitly out of scope per ADR, tracked as residual risk), `T-EVENTBUS-003` (out of scope, pre-existing), `SEC-TEMP-1..11` (in-scope subset listed in User Request 3).
* **Instructions discovered and applied**:
  * `.github/instructions/hve4isd/dotnet-enterprise.instructions.md` — `ILogger<T>` + `LoggerMessage` source generator for hot-path logging, `ProblemDetails` for API errors, idempotency key support for retried writes, ban on bare `catch (Exception)` swallow (existing `IdentifiedCommandHandler` swallow is pre-existing and out of scope to refactor beyond what the new result type requires), REQ-nnn test/trace conventions.
  * `.github/instructions/hve4isd/sdlc-artifacts.instructions.md` — requirement identifier citation conventions applied throughout this plan.
* **Skills discovered**: none of the listed dependency skills (aspire-*, dotnet-inspect, etc.) are needed for this change; validation uses `dotnet test` directly (see Dependencies).
* **Existing code read and directly relevant** (full list also appears per-task under References):
  * `src/Ordering.API/Application/Commands/CancelOrderCommandHandler.cs`, `CancelOrderCommand.cs`, `IdentifiedCommandHandler.cs`
  * `src/Ordering.API/Apis/OrdersApi.cs`, `src/Ordering.API/Program.cs` (confirms `.RequireAuthorization()` already applied to the whole `api/orders` group, so unauthenticated REQ-002 behavior (`401`) is already satisfied and needs only a verifying test, not new code)
  * `src/Ordering.Domain/AggregatesModel/OrderAggregate/Order.cs`, `OrderStatus.cs`
  * `src/Ordering.API/Application/DomainEventHandlers/OrderCancelledDomainEventHandler.cs`
  * `src/Ordering.API/Infrastructure/Services/IIdentityService.cs`, `IdentityService.cs` (identity = `sub` claim, matches `Buyer.IdentityGuid`)
  * `src/Ordering.Domain/AggregatesModel/BuyerAggregate/Buyer.cs`, `IBuyerRepository.cs`
  * `src/Ordering.API/Extensions/OrderingApiTrace.cs` (existing `LoggerMessage` source-generated trace pattern to extend)
  * `src/Catalog.API/IntegrationEvents/EventHandling/OrderStatusChangedToAwaitingValidationIntegrationEventHandler.cs`, `OrderStatusChangedToPaidIntegrationEventHandler.cs` (confirms no stock reservation/decrement exists before `Paid`)
  * `src/WebApp/Components/Pages/User/Orders.razor`, `OrdersRefreshOnStatusChange.razor`, `src/WebApp/Services/OrderingService.cs`
  * `tests/Ordering.UnitTests/Domain/OrderAggregateTest.cs`, `Builders.cs`, `Application/OrdersWebApiTest.cs`, `Application/IdentifiedCommandHandlerTest.cs`
  * `tests/Ordering.FunctionalTests/OrderingApiTests.cs`, `OrderingApiFixture.cs`, `AutoAuthorizeMiddleware.cs` (fixed test identity `9e3163b9-1ae6-4652-9dc6-7898ab7b7a00`, currently no seeded `Buyer` row correlated to it — see Assumption A3)

## Assumptions

* **A1 (REQ-004 interpretation)** — Because `AvailableStock` is only ever decremented at `Paid` and cancellation is only reachable from `Submitted`/`AwaitingValidation`, no order eligible for cancellation ever holds a stock reservation to release. REQ-004 is therefore satisfied by: (a) confirming the existing `OrderStatusChangedToCancelledIntegrationEvent` (already implemented, ADR Option B1, unchanged) is the "stock-release signal," (b) adding an explicit Catalog.API consumer that receives it (currently nothing in `Catalog.API` subscribes to this event) and logs receipt for future extensibility/observability, and (c) an inventory-side test proving `AvailableStock` is unchanged after cancellation. `{{tech-lead}}` should confirm this reading matches the charter's intent before P06 executes, since it does not implement a literal "reserved quantity returns to available stock" decrement/increment (none exists to reverse). This interpretation and its supporting evidence (no stock decrement before `Paid`, confirmed against `OrderStatusChangedToAwaitingValidationIntegrationEventHandler.cs`/`OrderStatusChangedToPaidIntegrationEventHandler.cs`) were independently re-verified during plan critique on 2026-09-16 and found correct; the `{{tech-lead}}` sign-off requirement above still stands and is not satisfied by the critique alone.
* **A2 (test framework per project)** — `tests/Ordering.UnitTests` uses MSTest (`[TestMethod]`); `tests/Ordering.FunctionalTests` already uses xUnit (`[Fact]`, `IClassFixture<T>`, `TestContext.Current.CancellationToken`). Per the enterprise instructions' "use the project's existing test framework," this plan uses `[TestMethod("REQ-nnn ...")]` for `Ordering.UnitTests` and `[Fact(DisplayName = "REQ-nnn ...")]` for `Ordering.FunctionalTests`, not a single framework across both.
* **A3 (functional-test buyer seeding)** — `AutoAuthorizeMiddleware` always authenticates as the fixed identity `9e3163b9-1ae6-4652-9dc6-7898ab7b7a00`; no functional test currently seeds a `Buyer`/`Order` pair correlated to that identity. P08 assumes `OrderingApiFixture`'s underlying `OrderingContext` can be seeded directly (in-memory/test database) with a `Buyer` (`IdentityGuid` = the fixed identity) and an `Order` (`BuyerId` = that buyer's id, status `Submitted`) before each ownership-scenario test; if the fixture does not currently expose a seeding hook, P08-T01 adds one (small, test-only addition under `tests/`).
* **A4 (HTTP status for ineligible-status rejection)** — REQ-001 requires a "client error" for `StockConfirmed`/`Paid`/`Shipped` cancel attempts but does not name a status code, and the ADR flags this as open ("`{{tech-lead}}` should confirm the exact HTTP status mapping"). This plan maps ineligible-status to `409 Conflict` (state conflict with current resource state) via `TypedResults.Problem(statusCode: 409)`, distinct from `403 Forbidden` (ownership) and `404 Not Found` (missing order). `{{tech-lead}}` confirms or overrides before P03 merges.
* **A5 (audit log sink)** — REQ-008 requires the audit trail to be "sufficient to answer who/when" and "structured/queryable by OrderId and identity"; this plan satisfies that with a structured `ILogger`/`LoggerMessage` entry (implemented as part of P02-T01) rather than a new persisted audit table, since the charter does not request a new durable audit store and the existing logging pipeline (per `dotnet-enterprise.instructions.md`) is already structured and queryable via the log sink. `{{code-owner}}` confirms this meets the audit requirement (SEC-TEMP-7) at PR gate, per the ADR's consequences note.
* **A6** — `{{tech-lead}}` and `{{code-owner}}` are the approving roles for design/PR gates, matching the ADR's own role placeholders.
* **A7 (REQ-006 automated coverage vs. manual verification)** — This plan resolves critique Finding 3 by choosing automated bUnit coverage (P07-T03) over deferring REQ-006 to manual QA only. `{{tech-lead}}` may still downgrade P07-T03 to manual verification only (e.g., if adding a new test project is judged disproportionate at PR time), but doing so requires an explicit risk acceptance recorded as one of the PR approval conditions (not a silent scope drop) — the reviewer must see and accept that a `must`-priority requirement's UI behavior is then verified only by `{{qa-owner}}` manual testing rather than an automated, requirement-tagged test.

## Implementation Checklist

### P01: Domain guard completeness (Order aggregate) — ✅ Complete (2026-09-16)
<!-- parallelizable: false -->

#### P01-T01: Add already-Cancelled no-op guard to `Order.SetCancelledStatus()` and complete unit coverage — ✅ Complete

**Goals**: Close ADR Option C2 at the domain layer so a cancel request against an order already in `Cancelled` status is a no-op (no re-raised `OrderCancelledDomainEvent`, no status change, no exception), while the existing `Paid`/`Shipped` guard exception behavior is preserved unchanged; add the unit test coverage for all five transition cases that currently does not exist (grep of `OrderAggregateTest.cs` shows zero cancel-related tests today).

**Requirements**: REQ-001, REQ-003, REQ-007

**Details**:
1. In `Order.SetCancelledStatus()`, add a guard clause before the existing `Paid`/`Shipped` check: `if (OrderStatus == OrderStatus.Cancelled) { return; }` (no domain event, no re-set of `Description`). Keep the existing `Paid`/`Shipped` → `StatusChangeException` branch and the `Submitted`/`AwaitingValidation` → `Cancelled` + `OrderCancelledDomainEvent` branch exactly as-is.
2. Add a `CancelledOrderBuilder`-style helper or extend `OrderBuilder` in `Builders.cs` with a way to construct an `Order` already in a given `OrderStatus` for test arrangement (reflection-free approach: drive the aggregate through its existing `Set*Status()` methods in sequence, e.g., `Submitted` → `SetAwaitingValidationStatus()` → `SetStockConfirmedStatus()` → `SetPaidStatus()` → `SetShippedStatus()`, to reach each target status realistically rather than reflecting into the private setter).
3. Add unit tests to `OrderAggregateTest.cs` for: `Submitted` → `Cancelled` (success + event raised), `AwaitingValidation` → `Cancelled` (success + event raised), `Paid` → cancel attempt (throws, status unchanged), `Shipped` → cancel attempt (throws, status unchanged), `Cancelled` → cancel attempt (no-op: no exception, no additional domain event, status remains `Cancelled`).
4. Add `// REQ-001`, `// REQ-003` comments above `SetCancelledStatus()` per the traceability convention.

**Tests** (`tests/Ordering.UnitTests/Domain/OrderAggregateTest.cs`, MSTest):
* `[TestMethod("REQ-003 SetCancelledStatus transitions Submitted to Cancelled and raises OrderCancelledDomainEvent")]`
* `[TestMethod("REQ-003 SetCancelledStatus transitions AwaitingValidation to Cancelled and raises OrderCancelledDomainEvent")]`
* `[TestMethod("REQ-001 SetCancelledStatus throws OrderingDomainException and leaves status unchanged for Paid orders")]`
* `[TestMethod("REQ-001 SetCancelledStatus throws OrderingDomainException and leaves status unchanged for Shipped orders")]`
* `[TestMethod("REQ-007 SetCancelledStatus is a no-op and raises no additional domain event when the order is already Cancelled")]`

**References**:
* [src/Ordering.Domain/AggregatesModel/OrderAggregate/Order.cs](../../../src/Ordering.Domain/AggregatesModel/OrderAggregate/Order.cs)
* [src/Ordering.Domain/AggregatesModel/OrderAggregate/OrderStatus.cs](../../../src/Ordering.Domain/AggregatesModel/OrderAggregate/OrderStatus.cs)
* [src/Ordering.Domain/Events/OrderCancelledDomainEvent.cs](../../../src/Ordering.Domain/Events/OrderCancelledDomainEvent.cs)
* [tests/Ordering.UnitTests/Domain/OrderAggregateTest.cs](../../../tests/Ordering.UnitTests/Domain/OrderAggregateTest.cs)
* [tests/Ordering.UnitTests/Builders.cs](../../../tests/Ordering.UnitTests/Builders.cs)

**Dependencies**: none (first phase).

---

### P02: Application-layer authorization, eligibility, and result type — ✅ Complete (2026-09-16)
<!-- parallelizable: false -->

#### P02-T01: Introduce `CancelOrderResult` and rewrite `CancelOrderCommandHandler` — ✅ Complete

**Goals**: Close `T-ORDERINGAPI-001` (Critical) by enforcing ownership inside `CancelOrderCommandHandler` (ADR Option A1); make the handler the single place that decides not-found vs. forbidden vs. ineligible-status vs. already-cancelled-no-op vs. success, so `OrdersApi` and the domain method stay unchanged in their own guard responsibilities. Also records the structured, identity-correlated audit log entry for successful cancellations (REQ-008 / SEC-TEMP-7 — formerly tracked as its own phase, P04, merged here because it is a one-line addition inside the same `Handle` method and the same test file; see the P04 heading below for the merge note).

**Requirements**: REQ-001, REQ-002, REQ-003, REQ-007, REQ-008

**Details**:
1. Add `public enum CancelOrderResult { Unknown, NotFound, Forbidden, AlreadyCancelled, IneligibleStatus, Success }` to `Ordering.API/Application/Commands/CancelOrderCommand.cs` (or a new adjacent file `CancelOrderResult.cs` in the same folder), with `Unknown` as the first (`= 0`) member so `default(CancelOrderResult)` is never a legitimate handler outcome — it is distinct from `NotFound` and every other named result — and so `IdentifiedCommandHandler.Handle`'s catch-all `return default;` produces a value (`Unknown`) that P03-T01 can map separately from a genuine `NotFound`, instead of a swallowed infrastructure failure being reported to the client as "order not found." `AlreadyCancelled` is now its own named value (previously folded into `Success`) so the already-`Cancelled`-no-op branch below is distinguishable from a fresh successful cancellation, without changing the HTTP response it produces (P03-T01 still maps it to `200 OK`, same as `Success`). Note: this change does not alter `IdentifiedCommandHandler`'s bare `catch { return default; }` itself (that pre-existing swallow is left as-is, out of scope per the enterprise instructions' note in Context Summary) — it only makes the swallowed result distinguishable from a legitimate `NotFound` at the API layer.
2. Change `CancelOrderCommandHandler` to `IRequestHandler<CancelOrderCommand, CancelOrderResult>`. Inject `IIdentityService` and `IBuyerRepository` alongside the existing `IOrderRepository`. `Handle` logic, in order:
   * Load the order via `_orderRepository.GetAsync(command.OrderNumber)`; if `null`, return `CancelOrderResult.NotFound`.
   * Resolve the caller's identity via `IIdentityService.GetUserIdentity()`; if `orderToUpdate.BuyerId` is `null` or the buyer looked up via `IBuyerRepository.FindByIdAsync(orderToUpdate.BuyerId.Value)` has an `IdentityGuid` that does not match the caller's identity, return `CancelOrderResult.Forbidden` without modifying the order. (Unauthenticated calls never reach the handler — `Program.cs` already applies `.RequireAuthorization()` to the whole `api/orders` group — so this branch only needs to cover "authenticated as someone else.")
   * If `orderToUpdate.OrderStatus == OrderStatus.Cancelled`, return `CancelOrderResult.AlreadyCancelled` without calling `SetCancelledStatus()` again (ADR Option C2; avoids re-raising `OrderCancelledDomainEvent`/duplicate integration event per REQ-007's second scenario; still logs the audit entry per step 3 below, since idempotent retries remain auditable).
   * If `orderToUpdate.OrderStatus` is `StockConfirmed`, `Paid`, or `Shipped`, return `CancelOrderResult.IneligibleStatus` without calling `SetCancelledStatus()` (REQ-001: the API must surface a client error, not rely on the domain exception path, which is swallowed by `IdentifiedCommandHandler`'s catch-all and currently surfaces as a `500`).
   * Otherwise (`Submitted`/`AwaitingValidation`), call `orderToUpdate.SetCancelledStatus()`, `await _orderRepository.UnitOfWork.SaveEntitiesAsync(cancellationToken)`, log the audit entry (step 3 below), and return `CancelOrderResult.Success`.
3. Add a new `[LoggerMessage]` partial method to `Ordering.API/Extensions/OrderingApiTrace.cs`: `LogOrderCancelledByCustomer(ILogger logger, int orderId, string buyerIdentity, DateTime cancelledAtUtc)` at `EventId = 4`, `EventName = "OrderCancelledByCustomer"`, `Level = LogLevel.Information`, structured message template `"Order {OrderId} was cancelled by customer identity {BuyerIdentity} at {CancelledAtUtc}"` (REQ-008 / SEC-TEMP-7, Assumption A5). Call it from `CancelOrderCommandHandler.Handle` immediately after the success path resolves (both the fresh-cancel and already-cancelled-no-op branches in step 2, so idempotent retries are still auditable — the no-op branch logs but does not re-persist state). Use `TimeProvider.System.GetUtcNow().UtcDateTime` (or an injected `TimeProvider`) rather than `DateTime.UtcNow` directly, per the enterprise instructions' "Patterns to Avoid" (`DateTime.Now`; use `TimeProvider`).
4. Update `CancelOrderIdentifiedCommandHandler : IdentifiedCommandHandler<CancelOrderCommand, CancelOrderResult>` and its `CreateResultForDuplicateRequest()` override to return `CancelOrderResult.Success` (unchanged semantics: a duplicate `x-requestid` returns the original success result, per ADR Option C1 — no new code needed beyond the type change since the override already hard-codes the "success" value).
5. Add `// REQ-002`, `// REQ-008` comments on `CancelOrderCommandHandler`.
6. Update the two now-broken existing tests in `tests/Ordering.UnitTests/Application/OrdersWebApiTest.cs` (`Cancel_order_with_requestId_success`, `Cancel_order_bad_request`, `Cancel_order_returns_problem_when_command_fails`) to mock `IMediator.Send(Arg.Any<IdentifiedCommand<CancelOrderCommand, CancelOrderResult>>(), default)` returning `CancelOrderResult.Success` / `CancelOrderResult.NotFound` (or whichever result is under test) instead of `bool`; this is a required fix, not new test coverage, because the handler's return type changes.

**Tests** (`tests/Ordering.UnitTests/Application/`, new file `CancelOrderCommandHandlerTest.cs`, MSTest):
* `[TestMethod("REQ-002 Handle returns Forbidden when the caller's identity does not match the order's buyer")]`
* `[TestMethod("REQ-002 Handle returns Success and cancels the order when the caller owns it")]`
* `[TestMethod("REQ-001 Handle returns NotFound when the order does not exist")]`
* `[TestMethod("REQ-001 Handle returns IneligibleStatus without calling SetCancelledStatus for StockConfirmed, Paid, and Shipped orders")]` (data-driven over the three statuses)
* `[TestMethod("REQ-007 Handle returns AlreadyCancelled without re-raising a domain event when the order is already Cancelled")]`
* `[TestMethod("REQ-008 Handle resolves the acting buyer's identity before cancelling")]` (verifies `IIdentityService.GetUserIdentity()` is invoked)
* `[TestMethod("REQ-008 Handle logs a structured audit entry with OrderId and the acting identity on successful cancellation")]` (assert via a captured `ILogger` substitute / `LoggerMessage`-generated call, not string matching on message text; moved here from the former P04-T01)

Updated existing tests (`tests/Ordering.UnitTests/Application/OrdersWebApiTest.cs`): `Cancel_order_with_requestId_success`, `Cancel_order_bad_request`, `Cancel_order_returns_problem_when_command_fails` adjusted for the `CancelOrderResult` return type (no new REQ display names needed since they already exist and their assertions do not change semantically, only the mocked return type).

**References**:
* [src/Ordering.API/Application/Commands/CancelOrderCommandHandler.cs](../../../src/Ordering.API/Application/Commands/CancelOrderCommandHandler.cs)
* [src/Ordering.API/Application/Commands/CancelOrderCommand.cs](../../../src/Ordering.API/Application/Commands/CancelOrderCommand.cs)
* [src/Ordering.API/Application/Commands/IdentifiedCommandHandler.cs](../../../src/Ordering.API/Application/Commands/IdentifiedCommandHandler.cs)
* [src/Ordering.API/Extensions/OrderingApiTrace.cs](../../../src/Ordering.API/Extensions/OrderingApiTrace.cs)
* [src/Ordering.API/Infrastructure/Services/IIdentityService.cs](../../../src/Ordering.API/Infrastructure/Services/IIdentityService.cs)
* [src/Ordering.API/Infrastructure/Services/IdentityService.cs](../../../src/Ordering.API/Infrastructure/Services/IdentityService.cs)
* [src/Ordering.Domain/AggregatesModel/BuyerAggregate/IBuyerRepository.cs](../../../src/Ordering.Domain/AggregatesModel/BuyerAggregate/IBuyerRepository.cs)
* [src/Ordering.Domain/AggregatesModel/BuyerAggregate/Buyer.cs](../../../src/Ordering.Domain/AggregatesModel/BuyerAggregate/Buyer.cs)
* [tests/Ordering.UnitTests/Application/OrdersWebApiTest.cs](../../../tests/Ordering.UnitTests/Application/OrdersWebApiTest.cs)
* [tests/Ordering.UnitTests/Application/IdentifiedCommandHandlerTest.cs](../../../tests/Ordering.UnitTests/Application/IdentifiedCommandHandlerTest.cs)

**Dependencies**: P01 (relies on the domain no-op guard existing so `IneligibleStatus`/`AlreadyCancelled` branches are consistent). No dependency on a separate P04 — the audit log call is now implemented directly in this task (see merge note under the P04 heading).

---

### P03: API-layer response mapping — ✅ Complete (2026-09-16)
<!-- parallelizable: false -->

#### P03-T01: Map `CancelOrderResult` to HTTP responses in `OrdersApi.CancelOrderAsync` — ✅ Complete

**Goals**: Replace the current binary `bool` → `200`/`500` mapping with a mapping that reflects REQ-001/REQ-002's required client-error semantics, without touching the unaffected `x-requestid` empty-GUID `400` path; also prove that an unexpected/swallowed exception still surfaces as `500`, not a misleading `404` (REQ-009 observability requirement — a failed attempt must remain distinguishable from a rejected one).

**Requirements**: REQ-001, REQ-002, REQ-009

**Details**:
1. Change `OrdersApi.CancelOrderAsync`'s `Results<...>` generic and body to handle a `CancelOrderResult` from `services.Mediator.Send(requestCancelOrder)` (where `requestCancelOrder` is now `IdentifiedCommand<CancelOrderCommand, CancelOrderResult>`):
   * `CancelOrderResult.Success` → `TypedResults.Ok()` (unchanged).
   * `CancelOrderResult.AlreadyCancelled` → `TypedResults.Ok()` (same response as `Success`; the client sees a successful idempotent cancel either way — see P02-T01).
   * `CancelOrderResult.NotFound` → `TypedResults.Problem(detail: "Order not found.", statusCode: 404)`.
   * `CancelOrderResult.Forbidden` → the **same** `TypedResults.Problem(detail: "Order not found.", statusCode: 404)` as `NotFound` (ADR amendment 2026-09-16; T-ORDERINGAPI-006). Both branches must produce byte-identical response bodies so a non-owner cannot distinguish an order they do not own from one that does not exist. Do not add a `403` branch. The handler result, audit trail, and P05 metrics keep `Forbidden` distinct.
   * `CancelOrderResult.IneligibleStatus` → `TypedResults.Problem(detail: "Order cannot be cancelled in its current status.", statusCode: 409)` (Assumption A4).
   * `CancelOrderResult.Unknown`, or any other unmatched/unexpected value (including the `default(CancelOrderResult)` that `IdentifiedCommandHandler.Handle`'s catch-all now produces when it swallows an unexpected exception) → keep the existing `500` `Problem` fallback for defense-in-depth. This is the only branch that maps to `500`; because `Unknown = 0` is no longer aliased to a named client-error result (see P02-T01 step 1), a swallowed infrastructure failure can no longer be misreported as `404 Not Found`.
2. Keep the existing empty-`x-requestid` → `400 BadRequest<string>` branch exactly as-is.
3. `Results<>` return type widens to `Results<Ok, BadRequest<string>, ProblemHttpResult>` — no new typed-result type needed since `ProblemHttpResult` already carries an arbitrary status code.
4. Update `tests/Ordering.UnitTests/Application/OrdersWebApiTest.cs`'s `Cancel_order_returns_problem_when_command_fails` (renamed/retargeted, see Tests below) and add the new forbidden/not-found/ineligible/already-cancelled cases.
5. Update `tests/Ordering.FunctionalTests/OrderingApiTests.cs`'s `CancelNonExistentOrderFails` (currently asserts `500 InternalServerError`; must be updated to assert `404 NotFound` once P02/P03 land) and add ownership/ineligible-status functional coverage (folded into P08 to keep this task's scope to the endpoint mapping itself; P08 owns the seeded-buyer scenarios).

**Tests** (`tests/Ordering.UnitTests/Application/OrdersWebApiTest.cs`, MSTest):
* `[TestMethod("REQ-002 CancelOrderAsync returns 404 with the not-found body when the command result is Forbidden")]` (assert status, `detail`, and `title` equal the `NotFound` response so nothing distinguishes the two)
* `[TestMethod("REQ-001 CancelOrderAsync returns 404 when the command result is NotFound")]`
* `[TestMethod("REQ-001 CancelOrderAsync returns 409 when the command result is IneligibleStatus")]`
* `[TestMethod("REQ-001 CancelOrderAsync returns 200 when the command result is AlreadyCancelled")]`
* `[TestMethod("REQ-009 CancelOrderAsync returns 500, not 404, when the command result is the default/Unknown value produced by a swallowed exception")]` (mocks `IMediator.Send` returning `default(CancelOrderResult)`/`CancelOrderResult.Unknown` — the value `IdentifiedCommandHandler.Handle`'s catch-all now produces for an unhandled exception — and asserts the response is `500 Problem`, proving the fix in P02-T01/P03-T01 keeps a swallowed exception from surfacing as a misleading `404`.)
* Existing `Cancel_order_with_requestId_success` and `Cancel_order_bad_request` updated for the `CancelOrderResult` type (see P02-T01) but keep their current names since their assertions are unchanged.
* Existing `Cancel_order_returns_problem_when_command_fails` retargeted to assert the `Unknown`/unexpected-value `500` fallback (rename to `Cancel_order_returns_problem_for_unexpected_result` for clarity; this is the non-REQ-009-tagged sibling of the explicit test above, kept for the pre-existing "unexpected value" coverage).

**References**:
* [src/Ordering.API/Apis/OrdersApi.cs](../../../src/Ordering.API/Apis/OrdersApi.cs)
* [src/Ordering.API/Program.cs](../../../src/Ordering.API/Program.cs)
* [tests/Ordering.UnitTests/Application/OrdersWebApiTest.cs](../../../tests/Ordering.UnitTests/Application/OrdersWebApiTest.cs)
* [tests/Ordering.FunctionalTests/OrderingApiTests.cs](../../../tests/Ordering.FunctionalTests/OrderingApiTests.cs)

**Dependencies**: P02 (requires `CancelOrderResult` to exist).

---

### P04: Audit logging (REQ-008 / SEC-TEMP-7) — **Merged into P02-T01**
<!-- parallelizable: false -->

This phase is no longer a separate implementation task. The prior circular sequencing between P02-T01 (which called the not-yet-existing `OrderingApiTrace.LogOrderCancelledByCustomer`) and P04-T01 (which depended on P02's handler existing) is resolved by folding the audit-log addition directly into **P02-T01** — the `[LoggerMessage]` member, the `Handle` call site, the `TimeProvider` usage, and the `REQ-008` audit-log test all now live under P02-T01's Details/Tests/References. REQ-008 remains fully covered; it is tracked against P02-T01 (see that task's Requirements line and Success Criteria below) rather than a standalone P04-T01. This heading is retained only so the phase numbering (P01, P02, P03, P05, P06, P07, P08) and cross-references elsewhere in this plan stay stable.

**Requirements**: REQ-008 (see P02-T01)

**Dependencies**: none (no independent task remains in this phase).

---

### P05: Cancel-endpoint and event-publish telemetry (REQ-009 / SEC-TEMP-9) — ✅ Complete (2026-09-16)
<!-- parallelizable: true -->

#### P05-T01: Add metrics distinguishing successful, rejected, and failed cancel attempts — ✅ Complete

**Goals**: Give the production gate a way to confirm error rates and publish success before the rollout is considered fully operational (REQ-009, SEC-TEMP-9), using the existing OpenTelemetry pipeline rather than a new observability stack.

**Requirements**: REQ-009

**Details**:
1. Add a `System.Diagnostics.Metrics.Meter` (e.g., `"eShop.Ordering.API"` name, matching the service name used by `builder.AddServiceDefaults()`'s OTel resource attribution) with three `Counter<long>` instruments: `order_cancellations_succeeded`, `order_cancellations_rejected` (tagged with a `reason` dimension: `not_found` | `forbidden` | `ineligible_status`), `order_cancellations_failed` (unexpected exceptions).
2. Increment the counters from `CancelOrderCommandHandler.Handle` at each corresponding return path.
3. Register the new `Meter` name with `AddOpenTelemetry().WithMetrics(m => m.AddMeter("eShop.Ordering.API"))` in the Ordering.API service registration (`Extensions/Extensions.cs` or wherever `AddApplicationServices()` configures OTel) so it is exported alongside existing traces/logs.
4. Leave `OrderStatusChangedToCancelledIntegrationEvent` publish success/failure observability as-is — the existing `IntegrationEventLogEF`/event-bus telemetry already covers publish attempts for every event type including this one (ADR Option B1 consequence: "No changes are required to `OrderingIntegrationEventService`... or the RabbitMQ event bus"), so this task does not duplicate that instrumentation.

**Tests** (`tests/Ordering.UnitTests/Application/CancelOrderCommandHandlerTest.cs`, MSTest, extending P02-T01's file):
* `[TestMethod("REQ-009 Handle increments the succeeded counter exactly once for a successful cancellation")]` (subscribe a `MeterListener` scoped to the test's `Meter` instance and assert the recorded measurement)
* `[TestMethod("REQ-009 Handle increments the rejected counter with reason=ineligible_status for a Paid order")]`
* Both new tests must be annotated `[DoNotParallelize]` (or the MSTest equivalent in use) — the `Meter`/`MeterListener` pair is scoped to a shared instrument name (matching the production `Meter` registration in step 3), so if MSTest runs these tests in parallel with each other or with other tests recording against the same `Meter`, one test's `MeterListener` can observe another test's measurements and produce flaky counts. Disabling parallelization for just these two methods (rather than injecting a per-test `Meter` instance, which would diverge from the production registration pattern) is the simplest way to keep the test deterministic without adding test-only DI seams.

**References**:
* [src/Ordering.API/Application/Commands/CancelOrderCommandHandler.cs](../../../src/Ordering.API/Application/Commands/CancelOrderCommandHandler.cs)
* [src/Ordering.API/Program.cs](../../../src/Ordering.API/Program.cs)

**Dependencies**: P02 (extends the same handler); no dependency on P04 (P04 no longer exists as a separate task — see the P04 heading's merge note), so P05 can proceed independently once P02 lands.

---

### P06: Catalog stock-release confirmation (REQ-004) — ✅ Complete (2026-09-16)
<!-- parallelizable: true -->

#### P06-T01: Add a Catalog.API consumer for `OrderStatusChangedToCancelledIntegrationEvent` and prove no stock drift — ✅ Complete

**Goals**: Give Catalog/Inventory an explicit subscriber to the cancellation event (currently none exists — only `WebApp` subscribes, for UI purposes) so the "stock-release signal" required by REQ-004 has a downstream consumer, and prove via test that cancelling a `Submitted`/`AwaitingValidation` order never changes `AvailableStock` (Assumption A1: there is nothing to release because nothing was reserved).

**Requirements**: REQ-004

**Details**:
1. Add `Catalog.API/IntegrationEvents/EventHandling/OrderStatusChangedToCancelledIntegrationEventHandler.cs` implementing `IIntegrationEventHandler<OrderStatusChangedToCancelledIntegrationEvent>` (mirroring the existing handler shape used by `OrderStatusChangedToPaidIntegrationEventHandler`/`OrderStatusChangedToAwaitingValidationIntegrationEventHandler`), reusing the existing `Catalog.API/IntegrationEvents/Events/` event contract (add an `OrderStatusChangedToCancelledIntegrationEvent` record there mirroring the `Ordering.API` one, since `Catalog.API` defines its own copies of shared integration event contracts per the existing pattern — confirm this duplication convention against `OrderStatusChangedToPaidIntegrationEvent.cs`/`OrderStatusChangedToAwaitingValidationIntegrationEvent.cs` before authoring).
2. The handler body logs receipt (`logger.LogInformation("Handling integration event: {IntegrationEventId} - ({@IntegrationEvent})", ...)`, matching the existing handlers' pattern) and explicitly does not decrement or increment `AvailableStock` for any item, since no reservation was ever taken (Assumption A1). Add a code comment explaining why, citing REQ-004 and this plan.
3. Register the new subscription in `Catalog.API/Extensions/Extensions.cs` (`eventBus.AddSubscription<OrderStatusChangedToCancelledIntegrationEvent, OrderStatusChangedToCancelledIntegrationEventHandler>();`), mirroring the existing registrations for sibling events.

**Tests** (new `tests/Catalog.UnitTests/` or equivalent existing Catalog test project — confirm exact project name during P06 execution since it was not enumerated in this research pass; if no Catalog unit test project exists, add the minimal one following the `Ordering.UnitTests` MSTest convention):
* `[TestMethod("REQ-004 Handling OrderStatusChangedToCancelledIntegrationEvent does not change AvailableStock for any catalog item")]`

**References**:
* [src/Catalog.API/IntegrationEvents/EventHandling/OrderStatusChangedToAwaitingValidationIntegrationEventHandler.cs](../../../src/Catalog.API/IntegrationEvents/EventHandling/OrderStatusChangedToAwaitingValidationIntegrationEventHandler.cs)
* [src/Catalog.API/IntegrationEvents/EventHandling/OrderStatusChangedToPaidIntegrationEventHandler.cs](../../../src/Catalog.API/IntegrationEvents/EventHandling/OrderStatusChangedToPaidIntegrationEventHandler.cs)
* [src/Ordering.API/Application/IntegrationEvents/Events/OrderStatusChangedToCancelledIntegrationEvent.cs](../../../src/Ordering.API/Application/IntegrationEvents/Events/OrderStatusChangedToCancelledIntegrationEvent.cs)
* [src/Ordering.API/Application/DomainEventHandlers/OrderCancelledDomainEventHandler.cs](../../../src/Ordering.API/Application/DomainEventHandlers/OrderCancelledDomainEventHandler.cs)

**Dependencies**: none on P01–P05 (independent service); can run in parallel with P02–P05. Depends only on the ADR's B1 decision (already implemented, unchanged).

---

### P07: WebApp cancel action and confirmation (REQ-006)
<!-- parallelizable: true -->

#### P07-T01: Add `OrderingService.CancelOrder` client method

**Goals**: Give the Orders page a typed client call to `PUT /api/orders/cancel` with the required `x-requestid` header, mirroring the existing `CreateOrder` method's shape.

**Requirements**: REQ-006

**Details**:
1. Add `public Task<HttpResponseMessage> CancelOrder(int orderNumber, Guid requestId)` to `OrderingService.cs`, building a `PUT` `HttpRequestMessage` to `remoteServiceBaseUrl + "cancel"` with the `x-requestid` header and a JSON body `{ orderNumber }` (matching `CancelOrderCommand`'s shape as seen by the API).
2. Return the raw `HttpResponseMessage` (not throw on non-success) so the calling Razor component can distinguish `200` from `404`/`409` and show an appropriate message (REQ-006 only requires a confirmation on success; this plan does not require specific error-message copy for the not-found/ineligible cases beyond "not going to crash the page," per Assumption — flag to `{{ux-owner}}` if specific error copy is desired later). The API never returns `403` for a non-owner (ADR amendment 2026-09-16), so the UI has no forbidden case to render.

**Tests**: none required at this layer beyond what P07-T03's bUnit component tests cover (this is a thin HTTP wrapper with no branching logic).

**References**:
* [src/WebApp/Services/OrderingService.cs](../../../src/WebApp/Services/OrderingService.cs)
* [src/Ordering.API/Application/Commands/CancelOrderCommand.cs](../../../src/Ordering.API/Application/Commands/CancelOrderCommand.cs)

**Dependencies**: P03 (needs the final response-status contract to know what to branch on).

#### P07-T02: Add cancel action and confirmation UI to the Orders page

**Goals**: Show a cancel action only for `Submitted`/`AwaitingValidation` orders; on success, show a confirmation and update the displayed status to `Cancelled` without a manual reload (REQ-006).

**Requirements**: REQ-006

**Details**:
1. In `Orders.razor`, add a cancel button/link per `<li class="orders-item">` row, visible only when `order.Status` is `"Submitted"` or `"AwaitingValidation"` (string compare against the existing `OrderRecord.Status` field, matching the page's existing `@order.Status.ToLower()` usage for the status pill).
2. Wire the action to a confirmation step (simple `confirm()`-style browser dialog or an inline confirm state toggle — follow existing WebApp UI conventions; no new component library dependency) then call `OrderingService.CancelOrder(order.OrderNumber, Guid.NewGuid())`.
3. On a `200` response, show a success message (e.g., a dismissible banner) and either re-fetch `orders` via `OrderingService.GetOrders()` or update the specific `OrderRecord`'s status locally to `"Cancelled"` so the row re-renders without a full page reload, satisfying "without requiring a manual reload."
4. On non-success, show a generic failure message (map `404`/`409` to short user-facing text; exact copy left to `{{ux-owner}}` review, not blocking for functional completion).
5. Confirm whether `OrdersRefreshOnStatusChange.razor` (already on the page) already triggers a re-render on the `OrderStatusChangedToCancelledIntegrationEvent` WebApp-side handler; if so, the local-state update in step 3 may be redundant with the SignalR/refresh mechanism already in place — read `OrdersRefreshOnStatusChange.razor` and `OrderStatusChangedToCancelledIntegrationEventHandler.cs` at execution time to avoid double-implementing the refresh path.

**Tests**: No Blazor/bUnit component test lives directly in this task; automated UI-behavior coverage for REQ-006 is provided by the new **P07-T03** (bUnit test project), which exercises this markup/wiring directly. This task's own changes are otherwise covered indirectly by P08's end-to-end API-contract test.

**References**:
* [src/WebApp/Components/Pages/User/Orders.razor](../../../src/WebApp/Components/Pages/User/Orders.razor)
* [src/WebApp/Components/Pages/User/OrdersRefreshOnStatusChange.razor](../../../src/WebApp/Components/Pages/User/OrdersRefreshOnStatusChange.razor)
* [src/WebApp/Services/OrderStatus/IntegrationEvents/EventHandling/OrderStatusChangedToCancelledIntegrationEventHandler.cs](../../../src/WebApp/Services/OrderStatus/IntegrationEvents/EventHandling/OrderStatusChangedToCancelledIntegrationEventHandler.cs)
* [src/WebApp/Services/OrderingService.cs](../../../src/WebApp/Services/OrderingService.cs)

**Dependencies**: P07-T01.

#### P07-T03: Add Ordering.WebApp bUnit tests for the cancel action

**Goals**: Give REQ-006 (a `must`-priority requirement) automated, requirement-tagged coverage of its UI-specific acceptance criteria, which are not observable from an API-only functional test (per critique Finding 3) — while following the enterprise instructions' "only run linters/builds/tests that already exist" rule by using the solution's already-adopted test tooling (MSTest.Sdk, matching `global.json`'s `"msbuild-sdks": { "MSTest.Sdk": "4.0.2" }` and `"test": { "runner": "Microsoft.Testing.Platform" }`) rather than introducing an unrelated test framework.

**Requirements**: REQ-006

**Details**:
1. Add a new test project `tests/WebApp.UnitTests/WebApp.UnitTests.csproj` using the `MSTest.Sdk` (`<Project Sdk="MSTest.Sdk/4.0.2">`, matching the SDK-style test-project convention already used by `tests/Ordering.UnitTests`), referencing `src/WebApp/WebApp.csproj` and adding the `bunit` NuGet package (bUnit is the standard Razor-component test library for MSTest/xUnit-based .NET solutions and requires no new runner — it runs under the same `Microsoft.Testing.Platform` runner already configured in `global.json`).
2. Add the project to `eShop.sln` (or the relevant solution filter) so it participates in `dotnet test`/CI alongside the other `tests/*` projects.
3. Write a focused bUnit test fixture for the `Orders.razor` cancel-action markup and code added in P07-T02: render the component with a `TestContext`, supply fake/stubbed `OrderingService`/order data via DI, and assert the three REQ-006 acceptance criteria below without needing a live API.
4. Keep the test project minimal and scoped to the cancel action (do not attempt to backfill bUnit coverage for the rest of `Orders.razor` or other WebApp pages — out of scope for this plan).

**Tests** (`tests/WebApp.UnitTests/`, new project, MSTest + bUnit):
* `[TestMethod("REQ-006 Cancel action is visible only for orders with status Submitted or AwaitingValidation")]` (data-driven: visible for `Submitted`/`AwaitingValidation`, absent for `StockConfirmed`/`Paid`/`Shipped`/`Cancelled`)
* `[TestMethod("REQ-006 A confirmation message is displayed after a successful cancellation")]`
* `[TestMethod("REQ-006 The order's displayed status updates to Cancelled without a page reload")]`

**References**:
* [src/WebApp/Components/Pages/User/Orders.razor](../../../src/WebApp/Components/Pages/User/Orders.razor)
* [src/WebApp/Services/OrderingService.cs](../../../src/WebApp/Services/OrderingService.cs)
* [tests/Ordering.UnitTests/Ordering.UnitTests.csproj](../../../tests/Ordering.UnitTests/Ordering.UnitTests.csproj) (SDK-style MSTest project convention to mirror)
* [global.json](../../../global.json) (`test.runner` = `Microsoft.Testing.Platform`, `msbuild-sdks.MSTest.Sdk` version to match)

**Dependencies**: P07-T02 (tests the markup/wiring it adds).

---

### P08: End-to-end functional coverage (REQ-005, REQ-007, cross-cutting REQ-002)
<!-- parallelizable: false -->

#### P08-T01: Functional tests for ownership, idempotency, and event-publish outcomes

**Goals**: Exercise the full `Ordering.API` pipeline (routing → `IdentifiedCommand` → handler → repository → outbox) via `WebApplicationFactory`, covering the scenarios that unit tests with mocked repositories cannot fully prove: real ownership mismatch against a seeded buyer, real duplicate-request-id idempotency, real already-cancelled idempotency, and real `IntegrationEventLogEF` outbox writes.

**Requirements**: REQ-002, REQ-005, REQ-007

**Details**:
1. Extend `OrderingApiFixture` (or add a seeding helper alongside it) to insert a `Buyer` row with `IdentityGuid = AutoAuthorizeMiddleware.IDENTITY_ID` and a second `Buyer` with a different `IdentityGuid`, plus `Order` rows owned by each, before the ownership tests run (Assumption A3). Keep seeding additive/idempotent so it does not break the existing unrelated tests in `OrderingApiTests.cs`.
2. Update `CancelNonExistentOrderFails` to assert `404 NotFound` (was `500 InternalServerError`; this is a required fix given the P02/P03 behavior change, not new coverage).
3. Add functional tests for: cancelling an order owned by a different buyer (`404`, with a response body identical to cancelling a non-existent order number, per the ADR amendment; the order must remain unmodified), cancelling an owned eligible order (`200`, and assert an `IntegrationEventLogEF` row exists for `OrderStatusChangedToCancelledIntegrationEvent` afterward — REQ-005), submitting the same `x-requestid` twice (second call returns the original `200` without a second integration-event-log row — REQ-007 scenario 1), submitting a new `x-requestid` against an order already `Cancelled` (returns `200` without adding a second integration-event-log row — REQ-007 scenario 2).

**Tests** (`tests/Ordering.FunctionalTests/OrderingApiTests.cs`, xUnit — see Assumption A2):
* `[Fact(DisplayName = "REQ-002 Cancelling another buyer's order returns 404 indistinguishable from a missing order and leaves the order unchanged")]`
* `[Fact(DisplayName = "REQ-005 Cancelling an eligible order publishes exactly one OrderStatusChangedToCancelledIntegrationEvent to the outbox")]`
* `[Fact(DisplayName = "REQ-007 Repeating the same cancel request id returns the original success result without a second outbox entry")]`
* `[Fact(DisplayName = "REQ-007 Cancelling an already-Cancelled order with a new request id succeeds without a second outbox entry")]`
* Updated: `CancelNonExistentOrderFails` now asserts `404 NotFound`.

**References**:
* [tests/Ordering.FunctionalTests/OrderingApiTests.cs](../../../tests/Ordering.FunctionalTests/OrderingApiTests.cs)
* [tests/Ordering.FunctionalTests/OrderingApiFixture.cs](../../../tests/Ordering.FunctionalTests/OrderingApiFixture.cs)
* [tests/Ordering.FunctionalTests/AutoAuthorizeMiddleware.cs](../../../tests/Ordering.FunctionalTests/AutoAuthorizeMiddleware.cs)
* [src/Ordering.API/Application/DomainEventHandlers/OrderCancelledDomainEventHandler.cs](../../../src/Ordering.API/Application/DomainEventHandlers/OrderCancelledDomainEventHandler.cs)

**Dependencies**: P02, P03 (needs the final handler/API contract to test against).

## Planning Log Reference

`.copilot-tracking/plans/logs/2026-09-16/order-cancellation-plan-log.md` (not created in this planning-only pass; create when implementation begins if deviations need tracking).

## Dependencies

* **Phase order**: P01 → P02 → P03 → P08 is a strict chain (each changes the contract the next depends on). P05 extends P02's handler/test file and can proceed independently once P02 lands, in parallel with P06 (P04 no longer exists as a separate phase — its work is merged into P02-T01; see the P04 heading's merge note). P06 (Catalog.API) has no dependency on the Ordering.API chain. P07 depends on P03 (needs the final HTTP contract); P07-T03 additionally depends on P07-T02 (tests the markup it adds).
* **Skills/tools**: none of the listed available skills are required; validation uses `dotnet build`/`dotnet test` against `tests/Ordering.UnitTests`, `tests/Ordering.FunctionalTests`, the new `tests/WebApp.UnitTests` (P07-T03), and whichever Catalog test project P06 confirms/creates.
* **Human confirmations required before/at implementation** (per Assumptions A1, A4, A5, A7): `{{tech-lead}}` on the REQ-004 interpretation (A1) and the `409` status choice (A4); `{{code-owner}}` on the audit-logging approach meeting SEC-TEMP-7 (A5) at PR gate; `{{tech-lead}}` on any decision to downgrade P07-T03 to manual-only verification (A7), recorded as an explicit PR approval condition if exercised.

## Success Criteria

* Every requirement REQ-001 through REQ-009 has at least one implemented task and at least one named test in this plan (cross-check: REQ-001 → P01-T01, P02-T01, P03-T01; REQ-002 → P02-T01, P03-T01, P08-T01; REQ-003 → P01-T01, P02-T01; REQ-004 → P06-T01; REQ-005 → P08-T01; REQ-006 → P07-T01, P07-T02, P07-T03; REQ-007 → P01-T01, P02-T01, P08-T01; REQ-008 → P02-T01; REQ-009 → P03-T01, P05-T01).
* `T-ORDERINGAPI-001` is closed by P02-T01 (ownership check enforced in the handler, the trust boundary identified by the security plan).
* `default(CancelOrderResult)` (`CancelOrderResult.Unknown`) is never mapped to the same HTTP status as a legitimate `CancelOrderResult.NotFound`; a swallowed exception in `IdentifiedCommandHandler.Handle` surfaces as `500`, proven by the `REQ-009`-tagged test in P03-T01.
* All new and updated tests pass under `dotnet test` for `Ordering.UnitTests`, `Ordering.FunctionalTests`, `WebApp.UnitTests` (P07-T03), and the Catalog test project touched by P06.
* No change to `Order.SetCancelledStatus()`'s existing `Paid`/`Shipped` exception-throwing behavior, `OrderingIntegrationEventService`, `IntegrationEventLogEF`, or the RabbitMQ event bus, per the ADR's stated consequences.
* `{{tech-lead}}` and `{{code-owner}}` have confirmed Assumptions A1, A4, A5, and A7 before the PR gate for the affected phases.

> AI-assisted content; review and validate before use.
