---
title: Release notes order-cancellation 1.0.0
description: Requirements delivered, changes included, and approvals for order-cancellation 1.0.0
ms.date: 2026-09-18
version: 1.0.0
commit: 0a2a9f95ccfa27ae52d89bae340496dbbd2005ac
---

## Summary

Release `1.0.0` of `order-cancellation` built from commit `0a2a9f95ccfa` on `release/order-cancellation-1.0.0` (risk tier medium).

Signed-in customers can now cancel their own orders from the Orders page while the order is still `Submitted` or `AwaitingValidation`. The Ordering API enforces ownership and status eligibility (`PUT /api/orders/cancel`: 200 on success or an already-cancelled order, 404 for a missing or another customer's order, 409 for an ineligible status), records a structured audit entry with the acting customer's identity, exposes `order_cancellations_*` counters, and publishes `OrderStatusChangedToCancelledIntegrationEvent` through the existing outbox so Catalog can react. Repeated requests with the same `x-requestid` are idempotent. Code-only change: no database migrations, no configuration or feature-flag changes.

## Requirements

| Requirement | Work item | Trace |
|-------------|-----------|-------|
| REQ-001 Cancellation is restricted to eligible order statuses | 1 | traced |
| REQ-002 Only the owning customer can cancel their order | 2 | traced |
| REQ-003 Domain enforces the Cancelled state transition with guards | 3 | traced |
| REQ-004 Reserved stock is released when an order is cancelled | 4 | traced |
| REQ-005 OrderCancelled integration event is published for downstream consumers | 5 | traced |
| REQ-006 Customer sees confirmation of a successful cancellation in the WebApp | 6 | traced |
| REQ-007 Repeated cancel requests for the same order are idempotent | 7 | traced |
| REQ-008 Cancellation is audited with the acting customer's identity | 8 | traced |
| REQ-009 Cancel endpoint and event-publish health are observable for rollout | 9 | traced |

## Changes

* .copilot-tracking/changes/2026-09-16/order-cancellation-changes.md ([order-cancellation-changes.md](.copilot-tracking/changes/2026-09-16/order-cancellation-changes.md))

## Approvals

* design gate: approved by alanp-msft on 2026-09-16
* plan gate: approved by alanp-msft on 2026-09-16
* pr gate: approved by alanp-msft on 2026-09-18

## Known Issues and Conditions

* No approval conditions were recorded on the design, plan, or pr gates.
* Suppressed security findings (governed, approver alanp-msft, expire 2026-12-17; see `evidence/security-2026-09-18.md`): checkov `CKV_OPENAPI_4`, `CKV_OPENAPI_5`, `CKV_OPENAPI_21` on the generated `src/Catalog.API/Catalog.API*.json` documents. Pre-existing upstream content; follow-up is to regenerate or remove the checked-in files.
* REQ-009: the `order_cancellations_succeeded` counter increments when the handler succeeds, before the surrounding transaction commits; a commit failure after that point is not counted as `failed`.
* REQ-006: whether the cancellation confirmation banner persists across the page's status-driven refresh is an open UX decision with the UX owner; the current behavior is a dismissible message that re-renders the row locally.
* REQ-002: the unauthenticated (401) path is enforced by `RequireAuthorization()` on the route group and is not covered by an executed test, because the functional-test host authenticates every request.
* Chain of custody: the plan-gate approval record now reports `stale` because the plan file received completion marks after approval; the design and pr approvals verify clean from a fresh clone. The pr-gate notes carry code-review item RV-003 (ADR status treated as accepted without editing the hashed file).

Evidence manifest: [manifest-1.0.0.json](manifest-1.0.0.json)

> AI-assisted content; review and validate before use.
