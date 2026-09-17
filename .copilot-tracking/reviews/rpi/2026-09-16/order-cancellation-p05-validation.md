<!-- markdownlint-disable-file -->
---
title: "P05 Validation: order-cancellation"
description: "Independent validation of the P05-T01 change record (cancel metrics) against the plan, security plan SEC-TEMP-9, and REQ-009"
ms.date: 2026-09-16
---

## Disposition: **Revise**, then **Pass** after remediation

First pass returned Revise on documentation accuracy and three REQ-009 interpretation points; all were remediated before commit. Independent runs after remediation: `dotnet build src\Ordering.API --no-restore` succeeded; `dotnet test tests\Ordering.UnitTests --no-restore` total 71, failed 0, succeeded 71. A minimal `WebApplication.CreateBuilder().Build()` host confirmed `IMeterFactory` is registered by default, so the new constructor dependency resolves at runtime (the P02 `TimeProvider` gap does not recur).

## Findings

1. **Should fix** — change record attributed the "decide how to count AlreadyCancelled" and "prefer IMeterFactory" guidance to the plan; both came from the orchestrator's task instructions. **Resolved**: attribution corrected.
2. **Should fix** — change record's file list omitted the plan and itself and said "exactly the three files". **Resolved**.
3. **Should fix (design)** — `order_cancellations_succeeded` increments inside the handler, but `TransactionBehavior` commits and dispatches the outbox after the handler returns; a commit failure is neither counted as `failed` nor subtracted, and a retrying Npgsql execution strategy could double-count. **Accepted and documented** as a known limitation in the change record: `succeeded` means "handler reached success", not "committed and published"; publish health stays on the existing outbox and event-bus telemetry. Follow-up candidate: count `failed` in a pipeline behavior.
4. **Should fix (design)** — `AlreadyCancelled → succeeded` made `succeeded` irreconcilable with outbox publishes when retries occur. **Resolved**: succeeded counter carries `outcome=cancelled|already_cancelled`; the three-instrument surface the plan specifies is preserved. New test `REQ-009 Handle increments the succeeded counter with outcome=already_cancelled for an idempotent retry`.
5. **Should fix (design)** — bare `catch` counted `OperationCanceledException` from a caller disconnect as `failed`. **Resolved**: `catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }` precedes the general catch. New test `REQ-009 Handle does not count a caller-cancelled request as failed`.
6. **Note** — metrics tests asserted exact counts on the target instrument but not absence on the others. **Resolved**: `MetricRecorder.AssertSingle` asserts exactly one measurement across all instruments, value 1, and the exact tag set.
7. **Note** — `TestMeterFactory` doc comment overstated equivalence with the caching production factory. **Resolved**.
8. **Note** — `CreateOrder` in the handler test still duplicates `OrderBuilder.WithStatus` (pre-existing from P02).
9. **Note** — per-request `IMeterFactory.Create` and `CreateCounter` return cached instances for identical options, so transient handlers create no new instruments; `reason` and `outcome` tags have fixed low cardinality.
10. **Note** — a second `AddOpenTelemetry().WithMetrics(...)` in Ordering.API is additive to the ServiceDefaults registration; the OTLP exporter picks up the new meter regardless of call order.

## Verified

* Meter `eShop.Ordering.API`; instruments `order_cancellations_succeeded` / `order_cancellations_rejected` / `order_cancellations_failed`; `reason` values `not_found`, `forbidden`, `ineligible_status`; increments on every return path; registration in `AddApplicationServices`, ServiceDefaults untouched; plan step 4 respected.
* `// REQ-009` on the class and at each instrumented branch; both plan display names present character for character; `[DoNotParallelize]` on all six metrics tests (required because `GlobalUsings.cs` enables method-level parallelization).
* `MeterListener` filters on `instrument.Meter.Name == MeterName`, created before the handler and disposed via `using`; the failed-counter test makes `IOrderRepository.GetAsync` throw and asserts `ThrowsExactlyAsync<InvalidOperationException>`; the forbidden-reason test closes P03 finding 6 (T-ORDERINGAPI-006 enumeration signal).
* Scope: exactly the handler, `Extensions.cs`, the handler test, the plan, and the change record changed.
* Plan `### P05` and `#### P05-T01` marked complete.

## Follow-ups

* P08 or CI: the REQ-008 container-resolution Fact now also proves `IMeterFactory` resolves from the real host.
* `{{code-owner}}` at the `pr` gate: accept finding 3 as a documented limitation or schedule the pipeline-behavior follow-up.
* Confirm whether Aspire's `EnrichNpgsqlDbContext` enables retry-on-failure, which sizes the double-count risk in finding 3.

> AI-assisted content; review and validate before use.
