<!-- markdownlint-disable-file -->
---
title: "P02 Validation: order-cancellation"
description: "Independent validation of the P02-T01 change record against the approved plan, ADR, security plan, and requirements REQ-001, REQ-002, REQ-003, REQ-007, REQ-008"
ms.date: 2026-09-16
---

## Disposition: **Revise**, then **Pass** after remediation

First pass returned Revise (one Must fix, three Should fix). Findings 1 through 4 were remediated in the same working tree before commit; the remediation is listed under Resolution. Independent runs after remediation: `dotnet build src\Ordering.API --no-restore` succeeded; `dotnet test tests\Ordering.UnitTests --no-restore` total 60, failed 0, succeeded 60. `tests/Ordering.FunctionalTests` cannot be restored or executed on this host (no `Aspire.AppHost.Sdk` in the package cache, no Docker); its new Fact remains unexecuted until CI or P08.

## Findings

1. **Must fix** — `tests/Ordering.FunctionalTests/OrderingApiTests.cs` — The new REQ-008 Fact referenced `IRequestHandler<>` without `using MediatR;` (the project's `GlobalUsings.cs` does not import it), so the file would not compile; the change record's "compile-only on this host" claim was untrue because the project was never compiled. **Resolved**: `using MediatR;` added; change record reworded to "not compiled or executed on this host".
2. **Should fix** — `src/Ordering.API/Apis/OrdersApi.cs` — The interim `!= Success → 500` mapping regressed REQ-007 at the HTTP layer: before P02 an already-cancelled order returned `200`; after P02 the handler returns `AlreadyCancelled`, which the interim branch mapped to `500`. **Resolved**: interim branch treats `Success` and `AlreadyCancelled` as `200`; still no `403`/`404`/`409`, so P03 scope is preserved.
3. **Should fix** — change record P02 Validation — Stated the functional test project "has no compile-time reference to `CancelOrderCommand`/`CancelOrderResult`", contradicted by the orchestrator's own addition. **Resolved**: paragraph rewritten.
4. **Should fix** — `tests/Ordering.UnitTests/Application/CancelOrderCommandHandlerTest.cs` — Only the identity-mismatch `Forbidden` branch was tested; `BuyerId == null`, buyer lookup returning `null`, and a `null` caller identity were uncovered although the ADR asks the code owner to confirm forbidden-branch coverage at the `pr` gate. **Resolved**: `[DataRow]`-driven test `REQ-002 Handle returns Forbidden when the order has no buyer, the buyer cannot be loaded, or the caller has no identity` added; it also asserts no audit entry is written for a forbidden attempt.
5. **Note (decision needed before P03)** — `CancelOrderCommandHandler.cs` returns `NotFound` before the ownership check, as the plan orders and the ADR maps (`404` vs `403`). Once P03 lands, an authenticated non-owner can distinguish existing from non-existing sequential order ids. Not listed in the security plan. `{{tech-lead}}` should either record it as an accepted residual risk next to A4 or have P03 collapse `Forbidden` to `404`.
6. **Note** — `Extensions.cs` `AddSingleton(TimeProvider.System)` is correct and safe; `WebApplicationFactory` overrides run after it, so a `FakeTimeProvider` still wins in tests. `TryAddSingleton` would be the more defensive form; optional.
7. **Note** — The functional Fact is a valid composition proof once it compiles: MediatR registers handlers transient, so `GetRequiredService` constructs the handler and its five dependencies without touching the database.
8. **Note** — `RecordingLogger` doc comment said "rendered message" while the implementation captures structured state (the better behavior). **Resolved**: comment corrected.
9. **Note** — `CreateOrder` in the handler test duplicates `OrderBuilder.WithStatus` because the builder cannot set `buyerId`; a `WithBuyerId` would remove the duplication when P05 extends this file.
10. **Note** — No test asserts the audit entry on the `AlreadyCancelled` path or its absence on `IneligibleStatus`/`NotFound`; the new finding-4 test covers absence on `Forbidden`. Coverage suggestion for P05, which extends the same file.
11. **Note** — Plan `### P02` heading and change-record title were not updated. **Resolved**.
12. **Note (P03 dependency)** — `OrdersWebApiTest.Cancel_order_returns_problem_when_command_fails` mocks `NotFound` and asserts `500`, true only under the interim mapping; P03-T01 must retarget it to `Unknown` as the plan says.
13. **Note (pre-existing, out of scope)** — `CreateResultForDuplicateRequest()` returns `Success` without re-running the ownership check (T-ORDERINGAPI-003, Low); unchanged by P02.

## Verified

* `CancelOrderResult` order `Unknown = 0, NotFound, Forbidden, AlreadyCancelled, IneligibleStatus, Success`; `CancelOrderCommand : IRequest<CancelOrderResult>`.
* Handler branch order NotFound → ownership → AlreadyCancelled → IneligibleStatus → cancel, save, audit, Success. Forbidden path performs no mutation and no save. Audit logged on Success and AlreadyCancelled only.
* Identity semantics: `IdentityService.GetUserIdentity()` returns the raw `sub` claim; WebApp creates orders with `UserId = sub`, so `Buyer.IdentityGuid` is the value compared. Ordinal comparison is correct for opaque ids; a `null` identity cannot match. **T-ORDERINGAPI-001 is closed at the handler boundary.**
* Audit entry carries `OrderId`, the owner's identity (logged after ownership passes), and a `TimeProvider` timestamp; `EventId = 4`, `EventName = "OrderCancelledByCustomer"`, `Information`, template matches the plan verbatim; no `DateTime.UtcNow`.
* `CancelOrderIdentifiedCommandHandler` updated; `CreateResultForDuplicateRequest() => Success`. `// REQ-002, REQ-008` on the handler, `// REQ-008` on the trace member.
* Seven plan test display names match character for character; REQ-007 test asserts `SaveEntitiesAsync` not invoked (P01 finding 4 addressed); IneligibleStatus test is data-driven over the three statuses; REQ-008 logging test asserts `EventId`, `EventName`, and structured `OrderId`/`BuyerIdentity`.
* `OrdersWebApiTest` cancel tests changed only the mocked generic type and return value.
* `OrdersApi.cs` diff is the type change plus the commented interim branch; P03 not pre-empted.
* Changed files match `git status --short`; every deviation (OrdersApi compile fix, `RecordingLogger`, `TimeProvider` registration and functional Fact, extra ownership test) is disclosed in the change record.

## Follow-ups

* P03: retarget `Cancel_order_returns_problem_when_command_fails` to `Unknown`; map `AlreadyCancelled` to `200`; record the finding 5 decision.
* P08 or CI: build and run `Ordering.FunctionalTests`, including the REQ-008 container-resolution Fact and a real cross-buyer `403`.
* Run the requirement-trace skill to confirm REQ-001/002/007/008 resolve to `CancelOrderCommandHandler.cs` and its tests.

> AI-assisted content; review and validate before use.
