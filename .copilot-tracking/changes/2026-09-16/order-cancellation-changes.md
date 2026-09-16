<!-- markdownlint-disable-file -->
---
title: "Order Cancellation Changes"
description: "Change record for the order-cancellation implementation plan, one section per completed phase"
ms.date: 2026-09-16
---

## Related Plan

`.copilot-tracking/plans/2026-09-16/order-cancellation-plan.md` — Phase `P01: Domain guard completeness (Order aggregate)`, task `P01-T01`.

## Implementation Date

2026-09-16

## Scope

This record covers **P01 only** (task P01-T01). No later phase (P02–P08) was started.

## Summary of Changes

Closed ADR Option C2 at the domain layer: `Order.SetCancelledStatus()` is now a no-op (no status change, no re-raised `OrderCancelledDomainEvent`) when the order is already `Cancelled`, while the existing `Paid`/`Shipped` guard-exception behavior is unchanged. Added the full five-case unit test matrix for `SetCancelledStatus()` transitions, which previously had zero cancel-related coverage, and added a status-driven test builder helper to construct orders at any target `OrderStatus` without reflection.

## Changes by Category

### Added

* `tests/Ordering.UnitTests/Domain/OrderAggregateTest.cs` — five new `[TestMethod]` tests for `SetCancelledStatus()`:
  * `SetCancelledStatus_from_Submitted_transitions_to_Cancelled_and_raises_event` — `[TestMethod("REQ-003 SetCancelledStatus transitions Submitted to Cancelled and raises OrderCancelledDomainEvent")]`
  * `SetCancelledStatus_from_AwaitingValidation_transitions_to_Cancelled_and_raises_event` — `[TestMethod("REQ-003 SetCancelledStatus transitions AwaitingValidation to Cancelled and raises OrderCancelledDomainEvent")]`
  * `SetCancelledStatus_from_Paid_throws_and_leaves_status_unchanged` — `[TestMethod("REQ-001 SetCancelledStatus throws OrderingDomainException and leaves status unchanged for Paid orders")]`
  * `SetCancelledStatus_from_Shipped_throws_and_leaves_status_unchanged` — `[TestMethod("REQ-001 SetCancelledStatus throws OrderingDomainException and leaves status unchanged for Shipped orders")]`
  * `SetCancelledStatus_from_Cancelled_is_noop_and_raises_no_additional_event` — `[TestMethod("REQ-007 SetCancelledStatus is a no-op and raises no additional domain event when the order is already Cancelled")]`
* `tests/Ordering.UnitTests/Builders.cs` — `OrderBuilder.WithStatus(OrderStatus targetStatus)` helper that drives the aggregate through its existing `Set*Status()` methods (`SetAwaitingValidationStatus`, `SetStockConfirmedStatus`, `SetPaidStatus`, `SetShippedStatus`, `SetCancelledStatus`) to reach any target status for test arrangement, without reflecting into the private `OrderStatus` setter. `Cancelled` is reached from `AwaitingValidation` (the last status still eligible for cancellation), not by first driving through `Paid`/`Shipped`.

### Modified

* `src/Ordering.Domain/AggregatesModel/OrderAggregate/Order.cs` — `SetCancelledStatus()`: added a guard clause (`if (OrderStatus == OrderStatus.Cancelled) { return; }`) before the existing `Paid`/`Shipped` → `StatusChangeException` branch, so a cancel request against an already-`Cancelled` order is a no-op. The `Paid`/`Shipped` guard-exception branch and the `Submitted`/`AwaitingValidation` → `Cancelled` + `OrderCancelledDomainEvent` branch are unchanged. Added `// REQ-001, REQ-003` traceability comment above the method.
* `.copilot-tracking/plans/2026-09-16/order-cancellation-plan.md` — marked `P01` and `P01-T01` headings `✅ Complete (2026-09-16)`.

### Removed

None.

## Requirements Addressed

* **REQ-001** — `Order.SetCancelledStatus()` continues to throw `OrderingDomainException` and leave status unchanged for `Paid`/`Shipped`; covered by 2 tests.
* **REQ-003** — Domain enforces the `Cancelled` transition (`Submitted`→`Cancelled`, `AwaitingValidation`→`Cancelled`) with the event raised; covered by 2 tests.
* **REQ-007** — Repeated cancellation of an already-`Cancelled` order is idempotent at the domain layer (no-op, no additional domain event); covered by 1 test.

## Additional or Deviating Changes

None. Implementation followed the plan's P01-T01 details exactly (guard clause placement, builder helper approach, five-test matrix, `// REQ-nnn` comment).

## Validation

Command:

```
dotnet test tests\Ordering.UnitTests --no-restore -v minimal
```

Result: **Passed** — `total: 48, failed: 0, succeeded: 48, skipped: 0` (43 pre-existing tests + 5 new tests), duration ~2.2s.

Note: initial `dotnet restore`/`dotnet test` failed because the sandboxed environment could not reach `https://api.nuget.org/v3/index.json` (TLS handshake failure to that specific Azure Front Door endpoint, while `https://www.nuget.org/api/v2` and other hosts were reachable). `nuget.config` was temporarily pointed at the working `https://www.nuget.org/api/v2` v2 feed to restore packages once, then reverted to the original `https://api.nuget.org/v3/index.json` source before running the final (green) test pass shown above; the committed `nuget.config` is unchanged from its original content.

## Release Summary

Phase P01 is the first of eight planned phases and has no independent release; it establishes the domain-layer no-op guard that P02 (`CancelOrderCommandHandler`) depends on. No later phase was started in this turn.

---

## P02: Application-layer authorization, eligibility, and result type

**Related Plan**: `.copilot-tracking/plans/2026-09-16/order-cancellation-plan.md` — Phase `P02: Application-layer authorization, eligibility, and result type`, task `P02-T01`.

**Implementation Date**: 2026-09-16

**Scope**: This addendum covers **P02 only** (task P02-T01). P01 (above) was already complete and committed; no later phase (P03–P08) was started. Per P02-T01 step 6, `src/Ordering.API/Apis/OrdersApi.cs` was touched only to the minimum extent required to keep it compiling against `CancelOrderCommand`'s new `CancelOrderResult` return type — it still maps every non-`Success` result to the pre-existing `500 Problem` fallback (unchanged behavior), deliberately deferring the full `CancelOrderResult` → HTTP status mapping (`403`/`404`/`409`) to P03-T01.

### Summary of Changes

Closed `T-ORDERINGAPI-001` (Critical) by moving ownership/eligibility/idempotency decisions for order cancellation into `CancelOrderCommandHandler`, which is now the single place that decides not-found vs. forbidden vs. ineligible-status vs. already-cancelled-no-op vs. success. Introduced `CancelOrderResult` (`Unknown = 0` first, so `default(CancelOrderResult)` is never a legitimate outcome) as the new return type for `CancelOrderCommand`/`CancelOrderCommandHandler`/`CancelOrderIdentifiedCommandHandler`. Added a structured, identity-correlated audit log entry (`OrderingApiTrace.LogOrderCancelledByCustomer`, `TimeProvider`-driven) for both fresh and idempotent (already-cancelled) successful cancellations, satisfying REQ-008/SEC-TEMP-7.

### Changes by Category

#### Added

* `src/Ordering.API/Application/Commands/CancelOrderResult.cs` — new `public enum CancelOrderResult { Unknown, NotFound, Forbidden, AlreadyCancelled, IneligibleStatus, Success }`, with `Unknown` as the first (`= 0`) member per plan step 1.
* `tests/Ordering.UnitTests/Application/CancelOrderCommandHandlerTest.cs` — new MSTest file (`// REQ-002, REQ-008` traceability comment) with 7 tests (one data-driven over 3 statuses via `[DataRow]`, so 9 test executions total):
  * `Handle_ReturnsForbidden_WhenCallerDoesNotOwnOrder` — `[TestMethod("REQ-002 Handle returns Forbidden when the caller's identity does not match the order's buyer")]`
  * `Handle_ReturnsSuccess_WhenCallerOwnsEligibleOrder` — `[TestMethod("REQ-002 Handle returns Success and cancels the order when the caller owns it")]`
  * `Handle_ReturnsNotFound_WhenOrderDoesNotExist` — `[TestMethod("REQ-001 Handle returns NotFound when the order does not exist")]`
  * `Handle_ReturnsIneligibleStatus_ForIneligibleOrderStatuses` (`[DataRow(OrderStatus.StockConfirmed)]`, `[DataRow(OrderStatus.Paid)]`, `[DataRow(OrderStatus.Shipped)]`) — `[TestMethod("REQ-001 Handle returns IneligibleStatus without calling SetCancelledStatus for StockConfirmed, Paid, and Shipped orders")]`
  * `Handle_ReturnsAlreadyCancelled_WhenOrderAlreadyCancelled` — `[TestMethod("REQ-007 Handle returns AlreadyCancelled without re-raising a domain event when the order is already Cancelled")]` — asserts `UnitOfWork.SaveEntitiesAsync` was **not** invoked (per P01 validation finding 4; does not rely on domain event counts, since the domain-level no-op guard from P01 makes an event-count assertion alone unable to distinguish a handler short-circuit from a domain no-op).
  * `Handle_ResolvesActingIdentity_BeforeCancelling` — `[TestMethod("REQ-008 Handle resolves the acting buyer's identity before cancelling")]`
  * `Handle_LogsStructuredAuditEntry_OnSuccessfulCancellation` — `[TestMethod("REQ-008 Handle logs a structured audit entry with OrderId and the acting identity on successful cancellation")]` — asserts via a hand-written `RecordingLogger : ILogger<CancelOrderCommandHandler>` test double that captures the `EventId` and structured tag values (`OrderId`, `BuyerIdentity`) synchronously during the `Log` call, not by string-matching the rendered message and not dependent on the `[LoggerMessage]` generator's pooled state surviving after the call returns.

#### Modified

* `src/Ordering.API/Application/Commands/CancelOrderCommand.cs` — `IRequest<bool>` → `IRequest<CancelOrderResult>`.
* `src/Ordering.API/Extensions/Extensions.cs` — `AddApplicationServices` now registers `services.AddSingleton(TimeProvider.System)` with a `// REQ-008` comment (orchestrator review fix; see Additional or Deviating Changes).
* `tests/Ordering.FunctionalTests/OrderingApiTests.cs` — added `using MediatR;` and `[Fact(DisplayName = "REQ-008 CancelOrderCommandHandler resolves from the Ordering.API container, including TimeProvider")]`, which resolves `IRequestHandler<CancelOrderCommand, CancelOrderResult>` from the real host's service provider (orchestrator review fix; not compiled or executed on this host, see Validation).
* `src/Ordering.API/Application/Commands/CancelOrderCommandHandler.cs` — `CancelOrderCommandHandler` rewritten as `IRequestHandler<CancelOrderCommand, CancelOrderResult>`; now injects `IIdentityService`, `IBuyerRepository`, `ILogger<CancelOrderCommandHandler>`, and `TimeProvider` alongside the existing `IOrderRepository`. `Handle` now: (1) returns `NotFound` if the order doesn't exist; (2) resolves the caller's identity and returns `Forbidden` if the order has no buyer or the buyer's `IdentityGuid` doesn't match; (3) returns `AlreadyCancelled` (logging the audit entry, no `SetCancelledStatus()` call) if already `Cancelled`; (4) returns `IneligibleStatus` for `StockConfirmed`/`Paid`/`Shipped` without calling `SetCancelledStatus()`; (5) otherwise calls `SetCancelledStatus()`, saves, logs the audit entry, and returns `Success`. Added `// REQ-002`, `// REQ-008` comment. `CancelOrderIdentifiedCommandHandler` now extends `IdentifiedCommandHandler<CancelOrderCommand, CancelOrderResult>` and `CreateResultForDuplicateRequest()` returns `CancelOrderResult.Success` (unchanged semantics).
* `src/Ordering.API/Extensions/OrderingApiTrace.cs` — added `// REQ-008` and the `[LoggerMessage(EventId = 4, EventName = "OrderCancelledByCustomer", Level = LogLevel.Information, ...)]` partial method `LogOrderCancelledByCustomer(ILogger logger, int orderId, string buyerIdentity, DateTime cancelledAtUtc)`, called with `TimeProvider.GetUtcNow().UtcDateTime` (never `DateTime.UtcNow` directly).
* `src/Ordering.API/Apis/OrdersApi.cs` — `CancelOrderAsync` updated to construct `IdentifiedCommand<CancelOrderCommand, CancelOrderResult>` instead of `IdentifiedCommand<CancelOrderCommand, bool>`, and to branch on `commandResult != CancelOrderResult.Success` instead of `!commandResult`. This is the minimal compile fix required by the widened return type; the full `CancelOrderResult` → HTTP status mapping (`403`/`404`/`409`) is explicitly deferred to P03-T01 and was **not** implemented here.
* `tests/Ordering.UnitTests/Application/OrdersWebApiTest.cs` — `Cancel_order_with_requestId_success` and `Cancel_order_bad_request` now mock `IMediator.Send(Arg.Any<IdentifiedCommand<CancelOrderCommand, CancelOrderResult>>(), default)` returning `CancelOrderResult.Success` (was `bool` `true`); `Cancel_order_returns_problem_when_command_fails` now returns `CancelOrderResult.NotFound` (was `bool` `false`) — the interim `OrdersApi` mapping above still routes any non-`Success` value to the existing `500 Problem` fallback, so this test's `500` assertion is unchanged. No new REQ display names were added to these three tests, matching the plan (their assertions are unchanged, only the mocked return type changed).
* `.copilot-tracking/plans/2026-09-16/order-cancellation-plan.md` — marked the `P02-T01` heading `✅ Complete`.

#### Removed

None.

### Requirements Addressed

* **REQ-001** — `Handle` returns `NotFound` (order doesn't exist) and `IneligibleStatus` (`StockConfirmed`/`Paid`/`Shipped`, without calling `SetCancelledStatus()`) instead of relying on the domain exception path; covered by 4 test executions (1 `NotFound` test + 3 `IneligibleStatus` `[DataRow]` executions).
* **REQ-002** — Ownership is enforced inside the handler: mismatched/absent buyer identity returns `Forbidden` without modifying the order; the owning caller succeeds; covered by 2 tests.
* **REQ-003** — No change to the P01 domain guard itself; P02-T01's `IneligibleStatus`/`AlreadyCancelled` handler branches are consistent with it (verified indirectly by the REQ-001/REQ-007 handler tests).
* **REQ-007** — Already-`Cancelled` orders return `AlreadyCancelled` without calling `SetCancelledStatus()` again (no re-persist, no duplicate domain event); covered by 1 test that asserts `UnitOfWork.SaveEntitiesAsync` was not invoked (addresses P01 validation finding 4).
* **REQ-008** — The acting identity is resolved before cancelling and a structured, `EventId`-tagged audit log entry (`OrderId`, `BuyerIdentity`, `CancelledAtUtc`, via `TimeProvider`) is recorded on every successful cancellation (fresh or idempotent no-op); covered by 2 tests.

### Additional or Deviating Changes

* **`TimeProvider` was not registered (found in orchestrator review, fixed before commit)** — the handler now takes `TimeProvider` by constructor injection per P02-T01 step 3, but `WebApplicationBuilder` does not register `TimeProvider` by default (verified with a minimal `WebApplication.CreateBuilder().Build().Services.GetService<TimeProvider>()`, which returns `null`). The unit tests pass `TimeProvider.System` directly, so they could not detect that MediatR would fail to construct `CancelOrderCommandHandler` on the first cancel request in the running service. Fix: `Extensions.AddApplicationServices` registers `AddSingleton(TimeProvider.System)`. A container-resolution test was added to `Ordering.FunctionalTests` (the only test project with the full host and `InternalsVisibleTo`) rather than the unit project, because a unit-level test would have to mirror the registration it is meant to prove and would pass with or without the fix.
* **`OrdersApi.cs` minimal compile fix** — not listed verbatim in P02-T01's Details, but required because `CancelOrderCommand`'s `IRequest<T>` type parameter changed from `bool` to `CancelOrderResult` (per P02-T01 step 2), so the caller in `OrdersApi.CancelOrderAsync` could no longer compile unchanged. Kept to the minimum: same `Results<Ok, BadRequest<string>, ProblemHttpResult>` signature, same single `500` fallback branch. After validation (P02 finding 2) the interim branch treats both `Success` and `AlreadyCancelled` as `200`, because the pre-P02 code returned `200` for an already-cancelled order (domain no-op, `SaveEntitiesAsync` → `true`) and mapping `AlreadyCancelled` to `500` would have regressed REQ-007 at the HTTP layer until P03. No `403`/`404`/`409` mapping was added; that is P03-T01's scope.
* **Extra ownership coverage (P02 validation finding 4)** — added a `[DataRow]`-driven test `REQ-002 Handle returns Forbidden when the order has no buyer, the buyer cannot be loaded, or the caller has no identity` covering the three `Forbidden` branches the plan's seven tests did not reach, and asserting no audit entry is written for a forbidden attempt. This is the evidence the ADR asks `{{code-owner}}` to confirm for T-ORDERINGAPI-001 at the `pr` gate.
* **REQ-008 audit test double** — the plan says to assert "via a captured `ILogger` substitute / `LoggerMessage`-generated call, not string matching on message text." An NSubstitute `ILogger` substitute alone could not be asserted against reliably here: the `[LoggerMessage]` source generator on this SDK emits a pooled `Microsoft.Extensions.Telemetry.Abstractions.LoggerMessageState` that is cleared/returned to its pool synchronously inside the generated method, so inspecting `ReceivedCalls()` after the `Handle` call returned observed an already-reset state object. A small hand-written `RecordingLogger : ILogger<CancelOrderCommandHandler>` test double was used instead, which captures the `EventId` and the structured tag key/value pairs (`OrderId`, `BuyerIdentity`) synchronously during the `Log<TState>` call — still asserting on structured data, not the rendered message text.

### Validation

Commands:

```
dotnet restore tests\Ordering.UnitTests -p:NuGetAudit=false
dotnet test tests\Ordering.UnitTests --no-restore
dotnet build src\Ordering.API --no-restore
```

Results:

* `dotnet test tests\Ordering.UnitTests --no-restore`: **Passed** — `total: 57, failed: 0, succeeded: 57, skipped: 0` (48 pre-existing P01 tests + 9 new `CancelOrderCommandHandlerTest` executions, including the 3-way `[DataRow]` test), duration ~1.2s.
* `dotnet build src\Ordering.API --no-restore`: **Build succeeded**, 0 Warning(s), 0 Error(s).

`tests/Ordering.FunctionalTests` was not built or run by the implementing agent (its existing tests only exercise the HTTP endpoint; the `500`→`404` assertion update is P03-T01's scope, per the plan). The orchestrator later added one Fact to it; see Post-review below.

Post-review (orchestrator, after the `TimeProvider` fix):

* `dotnet build src\Ordering.API --no-restore`: **Build succeeded**, 0 Error(s).
* `dotnet test tests\Ordering.UnitTests --no-restore`: **Passed** — `total: 57, failed: 0, succeeded: 57, skipped: 0`.
* `tests/Ordering.FunctionalTests` could not be restored on this host (`Aspire.AppHost.Sdk/13.5.3` is not in the local package cache and the NuGet feed is unreachable) and needs Docker to run. The new REQ-008 container-resolution test is therefore unexecuted; it must run in CI or at P08 before the `pr` gate.

### Release Summary

Phase P02 is the second of eight planned phases and has no independent release; it establishes the `CancelOrderResult` type and the authorization/eligibility/audit-logging handler logic that P03 (HTTP response mapping) and P05 depend on. No later phase (P03–P08) was started in this turn.

> AI-assisted content; review and validate before use.
