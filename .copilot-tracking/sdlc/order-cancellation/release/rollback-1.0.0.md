---
title: Rollback plan order-cancellation 1.0.0
description: Steps to return order-cancellation to the previous release if 1.0.0 must be withdrawn
ms.date: 2026-09-18
version: 1.0.0
status: ready
reviewed_by: alanp-msft
---

## Trigger

Start a rollback when, within the first 24 hours after deploying `1.0.0`, any of the following is observed and attributable to the cancellation path: `order_cancellations_failed` rising above 1% of `order_cancellations_succeeded + order_cancellations_rejected` over 15 minutes; Ordering.API 5xx rate on `PUT /api/orders/cancel` above 1% over 15 minutes; Ordering.API or WebApp health checks failing after the deployment; or an `IntegrationEventLog` backlog of `OrderStatusChangedToCancelledIntegrationEvent` entries stuck in `InProgress`/`PublishedFailed`. The technical owner ({{tech-lead}}) decides; the product owner ({{product-owner}}) is informed.

## Steps

1. Previous release: `main` at commit `dbc2140` ("chore(security): harden workflow permissions and clear secret-scan baseline (#17)"), the parent of merge commit `0a2a9f9`. Build artifacts are produced from source by the Aspire AppHost (`src/eShop.AppHost`); there is no separate artifact store for this sample.
2. Revert in source: `git revert -m 1 0a2a9f9` on `main`, open a pull request, let `pr-validation.yml` and `sdlc-gates.yml` run, merge. Redeploy Ordering.API, Catalog.API, and WebApp from the reverted `main` through the same deployment path used for `1.0.0` (Aspire/`azd` in this sample; per environment).
3. Database: no migrations in this release. `OrderStatus.Cancelled`, `IntegrationEventLog`, and the `ClientRequest` idempotency table already existed. No schema reversal is needed.
4. Feature flags and configuration: none introduced. Nothing to revert.
5. Verification: Ordering.API and WebApp `/health` return healthy; `PUT /api/orders/cancel` returns 404 (route removed) or the pre-release behavior; a signed-in user can load the Orders page and no cancel action is shown; `order_cancellations_*` meters stop emitting.

## Data and Compatibility

Orders cancelled by `1.0.0` remain in status `Cancelled`, which the previous release already reads and displays. Outbox rows for `OrderStatusChangedToCancelledIntegrationEvent` remain readable; the previous Catalog.API has no subscriber for that event and ignores it. No data written by this release becomes unreadable after rollback.

## Communication

The technical owner posts the decision and the reverting pull request link to the engineering channel and informs the product owner and support lead ({{support-lead}}) that self-service cancellation is withdrawn. Open an incident record referencing this plan and the reverting commit; close it after step 5 verification.

Set `status: ready` in the frontmatter once the release manager has reviewed this plan.

> AI-assisted content; review and validate before use.
