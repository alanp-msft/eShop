<!-- markdownlint-disable-file -->
---
title: "P03 Validation: order-cancellation"
description: "Independent validation of the P03-T01 change record (CancelOrderResult to HTTP mapping) against the amended plan, ADR, and requirements REQ-001, REQ-002, REQ-007, REQ-009"
ms.date: 2026-09-16
---

## Disposition: **Pass**

Two Should-fix edits to the change record and two optional test hardenings were applied before commit. Independent runs: `dotnet build src\Ordering.API --no-restore` succeeded; `dotnet test tests\Ordering.UnitTests --no-restore` total 65, failed 0, succeeded 65 (re-run after the hardenings, same counts).

## Findings

1. **Should fix** — change record P03 Validation — the test-count breakdown said "57 pre-existing + 1 retargeted + 7 new = 65"; the correct decomposition is 60 pre-existing executions plus 5 new tests. **Resolved.**
2. **Should fix** — change record P03 Additional or Deviating Changes — did not state that `tests/Ordering.FunctionalTests/OrderingApiTests.cs` `CancelNonExistentOrderFails` still asserts `500` and will fail the first time the functional suite runs. **Resolved**: called out as a known red test to be retargeted to `404` in P08.
3. **Note** — `Cancel_order_returns_problem_for_unexpected_result` and the REQ-009 test both exercised value `0` (`Unknown` and `default(CancelOrderResult)`), so the discard arm was not independently proven. **Resolved**: the sibling test now sends `(CancelOrderResult)99`.
4. **Note** — the REQ-002 body-identity test compared `StatusCode`, `Detail`, and `Title` only; identical by construction today because both results come from one switch arm, but not protective if the arm is later split. **Resolved**: also compares `Type`, `Instance`, and `Extensions.Count`.
5. **Note** — `AddProblemDetails()` appends a per-request `traceId` at write time; it varies for every response and carries no branch information, so it does not weaken the T-ORDERINGAPI-006 mitigation.
6. **Note** — no other client-observable distinction between `Forbidden` and `NotFound`: the API-layer "Sending command" log is server-side and precedes dispatch; the handler writes no audit entry for `Forbidden`; `409` and `200` are reachable only by the owner, so the ineligible-status branch cannot probe existence. Detecting enumeration attempts therefore depends on the P05 `rejected{reason=forbidden}` counter.
7. **Note** — the `// REQ-001, REQ-002, REQ-007, REQ-009` comment sits inside the method rather than on the class; the trace skill resolves either. The `AlreadyCancelled → 200` test carries REQ-001 per the plan's display name although REQ-007 is the more natural owner; the source comment covers REQ-007.
8. **Note** — P05 and P08 are not blocked by P03; P08's constraints (Aspire SDK not cached, Docker required, unexecuted REQ-008 Fact) are pre-existing host limitations.

## Verified

* `OrdersApi.CancelOrderAsync` switch: `Success | AlreadyCancelled → Ok()`; `NotFound | Forbidden →` one shared `Problem("Order not found.", 404)`; `IneligibleStatus → 409`; `_ → 500`. No `403` branch. Interim comment removed. Empty `x-requestid` `400` branch and `LogInformation` unchanged. Return signature unchanged. `ShipOrderAsync` untouched.
* All five plan display names present character for character. `Cancel_order_returns_problem_when_command_fails` renamed and retargeted (P02 finding 12 closed). No test expects `500` for `NotFound`.
* `git status --short` lists exactly the four files the change record names; functional tests untouched.
* Plan `### P03` and `#### P03-T01` headings marked complete; ADR amendment and T-ORDERINGAPI-006 cited correctly.
* REQ-002's `401` for unauthenticated callers remains covered by `.RequireAuthorization()`.

## Follow-ups

* P08: retarget `CancelNonExistentOrderFails` to `404`; run the REQ-008 container-resolution Fact; add the cross-buyer `404` functional test with body identity.
* P05: confirm `rejected{reason=forbidden}` is emitted so enumeration attempts stay detectable despite the silent `404`.
* `{{code-owner}}` at the `pr` gate can point the ADR's body-identity confirmation at `Cancel_order_returns_not_found_body_when_command_result_is_forbidden`.

> AI-assisted content; review and validate before use.
