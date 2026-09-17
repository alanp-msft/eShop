<!-- markdownlint-disable-file -->
---
title: "P07 Validation: order-cancellation"
description: "Independent validation of the P07 change record (WebApp cancel action, client method, and bUnit coverage) against the plan, Assumption A7, and REQ-006"
ms.date: 2026-09-17
---

## Disposition: **Pass**

Four Should-fix items; three applied before commit, one carried to the `pr` gate as a UX decision. Independent runs after remediation: `dotnet build src\WebApp --no-restore` succeeded; `dotnet test tests\WebApp.UnitTests --no-restore` total 12, failed 0, succeeded 12; `Ordering.UnitTests` 71/71; `Catalog.UnitTests` 1/1.

## Orchestrator review before validation

The implementing agent wired the cancel action through `@onclick` handlers. `Orders.razor` has no `@rendermode` and neither do `App.razor`, `Routes.razor`, or the layout; only `Chatbot` and `OrdersRefreshOnStatusChange` are interactive. Blazor event handlers on a statically rendered page never fire in the browser, so the agent's implementation was a dead button that its bUnit tests could not detect (bUnit dispatches handlers directly). The orchestrator rewrote the action as enhanced `method="post"` named forms following `CartPage`'s `_handler` hidden-input plus hidden `@formname` receiver pattern, with `[SupplyParameterFromForm(FormName = ...)]` bindings, and rewrote the tests to submit those forms. The validator confirmed the diagnosis and the pattern match.

## Findings

1. **Should fix** — the tests set the bound property on `cut.Instance` and submit the hidden receiver form, bypassing the exact wiring the rewrite was made to fix: a typo in the per-row form's `_handler` value or hidden field name would be a dead button again with green tests. **Resolved**: two tests assert the rendered per-row forms carry `method="post"`, an `_handler` value equal to the receiver's `@formname`, and a hidden field named after the bound property (via `nameof`).
2. **Should fix** — `class="button button-danger"` had no CSS rule anywhere in WebApp, so `Yes, cancel` rendered like `No`. **Resolved**: scoped rule in `Orders.razor.css` using the existing `--color-danger` tokens.
3. **Should fix** — per-row buttons were indistinguishable to assistive technology ("Cancel order, Cancel order, ..."). **Resolved**: `aria-label`s naming the order, mirroring `CartPage`.
4. **Should fix (UX decision)** — `OrdersRefreshOnStatusChange` calls `Nav.Refresh()` when the cancellation integration event arrives, which re-renders from the server and drops the success banner; status converges, so REQ-006 is met, but the message may be brief on a fast bus. **Carried to the `pr` gate** for `{{ux-owner}}`: accept as transient, or redirect to `user/orders?cancelled=N` and render the banner from the query string.
5. **Note** — `bunit` sat under the `Asp.Versioning` comment in `Directory.Packages.props`. **Resolved**: moved beside the other test-only packages.
6. **Note** — the failure banner used `role="status"`. **Resolved**: `role="alert"` for failures, `role="status"` for success.
7. **Note** — change-record wording said setting the property and submitting is "exactly" the SSR round-trip and listed `ItemPage`/`Checkout` as using the `_handler` pattern; only `CartPage` does. The record now describes the simulation's limits and the finding-1 tests close the gap.

## Verified

* Render mode: `Program.cs` enables interactive server support but no page opts in; the `@onclick` handlers would never have fired.
* Enhanced-form rewrite matches `CartPage`: per-row `method="post" data-enhance` form with `_handler`, `<AntiforgeryToken />`, and hidden value inputs; bare `<form @formname @onsubmit>` receivers. `FormName`-scoped `[SupplyParameterFromForm]` binds only for the matching handler, so the confirmation closes after `Yes, cancel`. `[StreamRendering]` plus POST is the same arrangement `CartPage` uses. `href="user/orders"` resolves against `<base href="/" />`. `app.UseAntiforgery()` is configured.
* T01: `PUT` to `.../cancel`, `x-requestid` header, `{ orderNumber }` body matching `CancelOrderCommand`, raw `HttpResponseMessage`; `// REQ-006`.
* T02: visibility rule, inline confirmation, success banner and local status replacement, generic failure copy, coexistence with `OrdersRefreshOnStatusChange` explained and not duplicated.
* T03: csproj mirrors `Catalog.UnitTests` with `bunit` in central props; `eShop.slnx` entry; the three plan display names character for character; `NullAntiforgeryStateProvider` acceptable for component tests; fresh `BunitContext` per test method makes parallel execution safe.
* Assertions cover: visibility across six statuses; no `PUT` before confirmation; exactly one `PUT` with `orderNumber` and a non-empty `x-requestid`; status pill `Cancelled` with no further cancel action; `409` renders the alert banner and leaves the status unchanged; form wiring names.
* Scope: only the listed WebApp, test, props, solution, plan, and change-record files changed. A7's risk-acceptance clause is not needed because automated coverage was delivered.

## Follow-ups

* `pr` gate: `{{ux-owner}}` decides finding 4.
* Manual browser pass of Cancel order → Yes, cancel under the Aspire host, including the integration-event refresh.
* P08 remains the authoritative contract check for the responses the UI branches on.

> AI-assisted content; review and validate before use.
