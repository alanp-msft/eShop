<!-- markdownlint-disable-file -->
---
title: "Order Cancellation Changes — P01"
description: "Change record for phase P01 (domain guard completeness) of the order-cancellation implementation plan"
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

> AI-assisted content; review and validate before use.
