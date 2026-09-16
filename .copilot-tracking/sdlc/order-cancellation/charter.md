---
title: "Order Cancellation Charter"
description: "Charter for allowing signed-in customers to cancel Submitted or AwaitingValidation orders from the Orders page"
ms.date: 2026-09-15
project: order-cancellation
sponsor: "{{product-owner}}"
risk_tier: medium
data_classification: confidential
ai_components: false
platforms:
  tracker: github
  repo_host: github
  tracker_repo: alanp-msft/eShop
stack:
  - dotnet
  - blazor
  - aspire
status: draft
created: 2026-09-15
---

## Problem and Outcome

Signed-in customers who submit an order have no self-service way to cancel it before it is paid or shipped, forcing them to contact support even when the order is still in `Submitted` or `AwaitingValidation` status. The measurable outcome is that a customer can cancel an eligible order from the Orders page, reserved stock is released, an `OrderCancelled` integration event is published for downstream consumers, and the customer sees a confirmation. Success is observed through a reduction in support-driven cancellations and via order-state telemetry showing cancellations only occur from the two eligible statuses.

## Scope

**In scope**
* A cancel action on the Orders page (WebApp) visible only for orders in `Submitted` or `AwaitingValidation` status.
* Ordering.API endpoint/command to cancel an order owned by the requesting signed-in customer.
* Ordering.Domain state transition to a `Cancelled` status with guards preventing cancellation of `Paid` or `Shipped` orders.
* Release of reserved stock associated with the cancelled order.
* Publishing an `OrderCancelled` integration event for downstream consumers (e.g., inventory, notifications).
* Confirmation feedback shown to the customer in the WebApp.

**Out of scope**
* Refund processing or any change to payment capture/settlement logic in PaymentProcessor.
* Cancellation of orders already `Paid` or `Shipped` (explicitly disallowed by design).
* Customer support / admin-initiated cancellation workflows.
* Changes to catalog or stock-reservation logic beyond releasing the reservation tied to the cancelled order.

**Assumptions**
* PaymentProcessor is an existing external/integrated system; no payment authorization or capture is triggered by this feature.
* Stock reservation release is achieved by publishing/handling existing integration events already supported by the Ordering and Catalog/Inventory bounded contexts, extended as needed for the cancellation path.
* Only the customer who owns the order (signed in) may cancel it; no admin override is in scope.

## Stakeholders

* Sponsor: {{product-owner}}
* Product Owner: {{product-owner}}
* Technical Owner: {{tech-lead}}
* Design Gate Approver: {{tech-lead}}
* PR Gate Approver: {{code-owner}}

## Risk Classification

* **Risk tier: medium** — The feature is customer-facing at scale (eShop storefront), sits adjacent to the payments domain (order status gating against `Paid`/`Shipped`, interaction with PaymentProcessor's order lifecycle), and processes confidential customer data (names, addresses, order history). No AI components are involved, which keeps it out of the `high` tier, but the payments-adjacency and customer PII exposure exceed `low`.
* **Data classification: confidential** — The system stores and processes customer names, addresses, and order history, which are personal data elements above `internal` sensitivity. No `restricted` categories (e.g., payment card data, government IDs) are handled directly by this feature.
* **AI components: false** — No model calls or AI-hosted behavior are part of this feature.

## Delivery Bindings

* Tracker: GitHub Issues (`alanp-msft/eShop`)
* Repository host: GitHub (`alanp-msft/eShop`)
* Target stack: .NET (Ordering.API, Ordering.Domain), Blazor (WebApp), Aspire orchestration
* Environments: existing eShop dev/test environments (no new environment introduced by this feature)

## Lifecycle Plan

Gates applicable at `risk_tier: medium`:

* **Design gate**: required. Reviews the cancellation state-machine guard (`Submitted`/`AwaitingValidation` only), the `OrderCancelled` integration event contract, and stock-release behavior before implementation. A Security Plan is required ahead of this gate per HVE Core routing for `medium`/`high` tiers.
* **PR gate**: required. Standard code review plus security evidence triage (SARIF) prior to merge.
* **Release gate**: required. Standard release evidence and manifest binding before deployment.
* **Production gate**: required. Confirms rollout health (error rates on cancel endpoint, event publish success) before considering the change fully operational.

> AI-assisted content; review and validate before use.
