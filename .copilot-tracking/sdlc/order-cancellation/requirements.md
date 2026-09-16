---
title: "Order Cancellation Requirements"
description: "Testable requirements for allowing signed-in customers to cancel Submitted or AwaitingValidation orders from the Orders page"
ms.date: 2026-09-15
---

## REQ-001 Cancellation is restricted to eligible order statuses

Acceptance: Given an order with `OrderStatus` of `Submitted` or `AwaitingValidation`, a cancel request transitions the order to `Cancelled`; given an order with `OrderStatus` of `StockConfirmed`, `Paid`, or `Shipped`, a cancel request is rejected and the order status is unchanged. `Order.SetCancelledStatus()` continues to throw an `OrderingDomainException` when invoked against `Paid` or `Shipped` orders, and the API surfaces this as a client error rather than a 500.
Priority: must
Source: Charter > Problem and Outcome; Charter > Scope > In scope

## REQ-002 Only the owning customer can cancel their order

Acceptance: A signed-in customer who submits a cancel request for an `orderNumber` they own (their `BuyerId`/identity matches the order) succeeds when the order is eligible; a signed-in customer who submits a cancel request for an order owned by a different buyer receives a `403 Forbidden` (or equivalent not-authorized result) and the order is not modified. Unauthenticated requests to the cancel endpoint are rejected with `401 Unauthorized`.
Priority: must
Source: Charter > Scope > Assumptions; Charter > Scope > In scope

## REQ-003 Domain enforces the Cancelled state transition with guards

Acceptance: `Order.SetCancelledStatus()` sets `OrderStatus` to `Cancelled` and raises an `OrderCancelledDomainEvent` only when the current status is not `Paid` or `Shipped`; unit tests cover both the allowed transitions (`Submitted`→`Cancelled`, `AwaitingValidation`→`Cancelled`) and the two disallowed transitions, asserting the guard exception and unchanged status.
Priority: must
Source: Charter > Scope > In scope; Charter > Risk Classification

## REQ-004 Reserved stock is released when an order is cancelled

Acceptance: When an order transitions to `Cancelled` from `AwaitingValidation` or `Submitted`, a stock-release signal (integration event or equivalent) is emitted so Catalog/Inventory releases any reservation tied to that order's items; an inventory-side test confirms reserved quantities return to available stock after the cancellation event is processed, with no change to catalog item counts for orders that were never reserved (e.g., cancelled while still `Submitted`).
Priority: must
Source: Charter > Scope > In scope; Charter > Scope > Assumptions

## REQ-005 OrderCancelled integration event is published for downstream consumers

Acceptance: After a successful cancellation, `OrderingIntegrationEventService` publishes an `OrderStatusChangedToCancelledIntegrationEvent` containing `OrderId`, `OrderStatus` (`Cancelled`), `BuyerName`, and `BuyerIdentityGuid`; downstream subscribers (e.g., WebApp order-status handler) receive and process exactly one event per cancellation, verified via an integration test that asserts the event is enqueued/published on the event bus after `SaveEntitiesAsync` commits.
Priority: must
Source: Charter > Problem and Outcome; Charter > Scope > In scope

## REQ-006 Customer sees confirmation of a successful cancellation in the WebApp

Acceptance: On the Orders page (`/user/orders`), a cancel action is visible only for orders whose status is `Submitted` or `AwaitingValidation`; after the customer confirms cancellation and the API call returns success, the UI displays a confirmation message and the order's displayed status updates to `Cancelled` (via `OrdersRefreshOnStatusChange` or a page refresh) without requiring a manual reload.
Priority: must
Source: Charter > Scope > In scope; Charter > Problem and Outcome

## REQ-007 Repeated cancel requests for the same order are idempotent

Acceptance: Submitting the same cancel request (same `x-requestid`) more than once returns the original success result without re-executing the cancellation logic or raising a duplicate `OrderCancelledDomainEvent`/integration event, per the existing `IdentifiedCommandHandler`/`CancelOrderIdentifiedCommandHandler` pattern; submitting a new cancel request (different `x-requestid`) against an order already in `Cancelled` status returns success without error and does not publish a second `OrderCancelled` integration event or re-trigger stock release.
Priority: must
Source: Charter > Problem and Outcome; Charter > Scope > In scope

## REQ-008 Cancellation is audited with the acting customer's identity

Acceptance: Each successful cancellation records, in application logs or persisted order history, the order number, the identity (`BuyerIdentityGuid`/user id) of the customer who initiated the cancellation, and a timestamp, sufficient to answer "who cancelled this order and when" during support or audit review; log entries are structured (not free text only) so they can be queried by `OrderId` and identity.
Priority: should
Source: Charter > Risk Classification; Charter > Lifecycle Plan

## REQ-009 Cancel endpoint and event-publish health are observable for rollout

Acceptance: The cancel endpoint emits metrics/telemetry distinguishing successful cancellations, rejected (ineligible status) attempts, and failed (error) attempts; `OrderCancelled` integration event publish success/failure is observable via existing event-bus telemetry, enabling the production gate to confirm error rates and publish success before the change is considered fully operational.
Priority: should
Source: Charter > Lifecycle Plan > Production gate

> AI-assisted content; review and validate before use.
