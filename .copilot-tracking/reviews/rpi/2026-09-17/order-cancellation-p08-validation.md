<!-- markdownlint-disable-file -->
---
title: "P08 Validation: order-cancellation"
description: "Independent validation of the P08-T01 change record (functional tests for ownership, idempotency, and outbox) against the plan, ADR amendment, and REQ-002, REQ-005, REQ-007"
ms.date: 2026-09-17
---

## Disposition: **Revise**, then **Pass** after remediation

The tests could not be executed on this host (no Docker), so the validator traced the success path through the real pipeline instead of trusting a green compile. That trace found a latent production defect that would have failed three of the four new tests wherever they ran. Findings 1 and 3 were remediated before commit; finding 2 is a `pr` gate condition. Independent runs after remediation: `dotnet build tests\Ordering.FunctionalTests --no-restore` succeeded; `dotnet test tests\Ordering.UnitTests --no-restore` total 72, failed 0; `Catalog.UnitTests` 1/1; `WebApp.UnitTests` 12/12; `Catalog.API` builds.

## Findings

1. **Must fix** — `IntegrationEventLogService<TContext>` resolved integration event types from `Assembly.GetEntryAssembly()`. Under `WebApplicationFactory` the entry assembly is the test project, which defines no `*IntegrationEvent` types, so `RetrieveEventLogsPendingToPublishAsync` passed a `null` type to `JsonSerializer.Deserialize` inside `TransactionBehavior`'s post-commit publish step, outside the RabbitMQ try/catch. Every successful cancel (and the pre-existing `AddNewOrder`) would return `500` in the functional host while the database state was already correct. **Resolved** in `src/IntegrationEventLogEF/Services/IntegrationEventLogService.cs`: event types are gathered from the entry assembly, the `TContext` assembly, and loaded assemblies, and resolved by `FullName` with a short-name fallback and an explicit exception instead of a null type. A new `Ordering.UnitTests` test reproduces the foreign-entry-assembly condition and was verified red without the fix (`ArgumentNullException: returnType`) and green with it. This is a `src/` change outside the task's declared scope, made by the orchestrator on the tech lead's standing authority to fix defects the plan's own verification exposes; the change record discloses it.
2. **Must fix (verify at the `pr` gate)** — there is reason to doubt CI executes `Ordering.FunctionalTests` at all: the upstream `pr-validation.yml` run for the same solution filter completes in about 90 seconds, inconsistent with four Aspire fixtures starting Postgres containers, and the finding-1 defect would have failed upstream's `AddNewOrder` if the suite ran. **Carried**: the `pr` gate reviewer must confirm from CI output a non-zero `Ordering.FunctionalTests` test count including the four new display names. A green job with zero functional tests executed is not evidence for REQ-002/005/007.
3. **Should fix** — `GetStoredOrdersWithOrderId` asserted `GET api/orders/1` is `404`; the seeding helper makes id `1` reachable (HiLo from 1, no buyer filter in `OrderQueries`). **Resolved**: the test now requests `int.MaxValue`.
4. **Note** — REQ-002's `401` clause is not covered by P08 because `AutoAuthorizeMiddleware` authenticates every request; it stays covered by `.RequireAuthorization()` on the route group. Recorded for the trace matrix.
5. **Note** — REQ-005's "subscribers receive exactly one event" is asserted at the outbox row, as the plan scoped it, not at bus delivery. Recorded.
6. **Note** — change-record wording: the csproj was listed as modified though untouched, and isolation was attributed to "the same class instance". **Resolved**.
7. **Note** — `NewSubmittedOrder` uses `DateTime.UtcNow` for a card expiry that is never persisted; harmless.

## Will it pass in CI?

After remediation: the success path no longer throws; RabbitMQ is absent in the fixture but `PublishAsync` fails inside the try/catch and the entry is marked `PublishedFailed`, which does not affect status codes and still counts as one outbox row. Docker-dependent startup (Postgres, Identity.API, migrations) is the pre-existing fixture pattern. Expected: all four new tests and `CancelNonExistentOrderFails` pass, **provided the suite executes** (finding 2).

## Verified

* Seeding rows are insertable: `Buyer(identity, name)` satisfies the schema; the `Order` constructor sets `Submitted`, owned `Address` populated, nullable FKs, no items required; base `SaveChangesAsync` (not `SaveEntitiesAsync`) means no domain-event dispatch, which is correct because dispatching `OrderStartedDomainEvent` would try to create a second buyer for the same identity. Migrations and seed run in `MigrationHostedService` during host start.
* Identity flow: `AutoAuthorizeMiddleware` adds `sub = IDENTITY_ID`; `IdentityService.GetUserIdentity()` reads `sub`; the seeded owner matches → `Success`, the other buyer → `Forbidden` → the same 404 problem as `NotFound`. `traceId` is the only per-request field and is stripped before comparison.
* Outbox: one `OrderCancelledDomainEvent` → one `IntegrationEventLogEntry` via `AddAndSaveEventAsync` inside the transaction; `Set<IntegrationEventLogEntry>()` is mapped by `UseIntegrationEventLogs()`; `EventTypeName` is the full type name; `Content` is PascalCase so `GetProperty("OrderId")` resolves.
* Idempotency: `ClientRequest` commits atomically with the first cancel; a repeated id short-circuits to `Success`; a new id against a `Cancelled` order returns `AlreadyCancelled` with no new outbox row.
* Isolation: one test class, sequential methods, fresh orders per seed call; `AddNewOrder` fails before creating a buyer and never touches the seeded ones.
* Plan conformance: four display names character for character; `CancelNonExistentOrderFails` asserts `404`; `eShop.Web.slnf` adds `Catalog.UnitTests` and `WebApp.UnitTests`; `// REQ-nnn` comments present; plan `### P08` and `#### P08-T01` marked complete; the change record states plainly that the functional tests were compiled, not executed.

## Follow-ups

* `pr` gate: confirm finding 2 from CI output; record findings 4 and 5 in the trace evidence.
* Consider proposing the `IntegrationEventLogService` fix upstream to dotnet/eShop.

> AI-assisted content; review and validate before use.
