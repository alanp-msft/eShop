---
title: "Lessons: order-cancellation"
description: "Accumulating lessons for the order-cancellation project; entries are never deleted, only marked resolved"
ms.date: 2026-09-18
project: order-cancellation
---

## Cycle 1 (release 1.0.0, retro 2026-09-18)

### L-001 A test that compiled but never executed is not evidence

Evidence: [tests-2026-09-17.md](../evidence/tests-2026-09-17.md) Runs item 4 (functional Facts pending CI); [review.md](../../../reviews/code-reviews/order-cancellation/review.md) RV-001; [p06 validation](../../../reviews/rpi/2026-09-16/order-cancellation-p06-validation.md) (first execution failed on a pgvector mapping)
Destination: artifact
Owner: Test Engineer
Status: open
Routed to:

### L-002 Dependency registration needs a container-resolution test in the real host

Evidence: [p02 validation](../../../reviews/rpi/2026-09-16/order-cancellation-p02-validation.md) (TimeProvider not registered by the host, invisible to unit tests); [p08 validation](../../../reviews/rpi/2026-09-17/order-cancellation-p08-validation.md) (IntegrationEventLogService resolved event types from the entry assembly and failed under WebApplicationFactory)
Destination: artifact
Owner: Implementation Validator
Status: open
Routed to:

### L-003 Check the Blazor render mode before wiring interactivity

Evidence: [p07 validation](../../../reviews/rpi/2026-09-17/order-cancellation-p07-validation.md) (`@onclick` handlers were dead under static server rendering; rewritten as enhanced forms); [review.md](../../../reviews/code-reviews/order-cancellation/review.md) RV-007
Destination: artifact
Owner: Tech Lead
Status: open
Routed to:

### L-004 A validator that cannot execute a test must trace its runtime path instead

Evidence: [p08 validation](../../../reviews/rpi/2026-09-17/order-cancellation-p08-validation.md) (latent bug found by reading the host composition, not by running the suite); [tests-2026-09-17.md](../evidence/tests-2026-09-17.md) environment constraint (no Docker)
Destination: artifact
Owner: Implementation Validator
Status: open
Routed to:

### L-005 Test result filenames carried user and host identifiers into the repository

Evidence: change record P08 (TRX files renamed before commit); the data-boundary scanner did not flag `Users/<name>` path segments in a generated evidence file ([security-2026-09-18.md](../evidence/security-2026-09-18.md) Inputs line, corrected by hand)
Destination: artifact
Owner: Overlay Maintainer
Status: open
Routed to:

### L-006 The installed overlay lagged the source; upgrade before the verify stage

Evidence: [plan.json](../gates/plan.json) reads `stale` after completion marks (fixed upstream by evidence-hash stripping); [manifest-1.0.0.json](../release/manifest-1.0.0.json) reports the plan `approved` without re-hashing; generated release notes said no suppressed findings while [security-2026-09-18.md](../evidence/security-2026-09-18.md) lists six
Destination: artifact
Owner: SDLC Conductor
Status: open
Routed to:

### L-007 Run the security scan once on the baseline before the first feature reaches the pr gate

Evidence: SDLC Gates run 35317203405 failed on two gitleaks hits and nine checkov findings, none introduced by the change; hygiene shipped as a separate pull request (#17) and a governed suppression
Destination: artifact
Owner: Onboarding Guide
Status: open
Routed to:

### L-008 Four CI template defects surfaced on the first real pr-gate run

Evidence: `.github/workflows/sdlc-gates.yml` history on `feature/order-cancellation`: gitleaks `--source /repo` fingerprints never matched `.gitleaksignore`; `GATE_FLAGS` empty string is falsy so pull requests always required approval; `triage-sarif` Inputs line wrote a local path; `.gitignore` `[Rr]elease/` hid `.copilot-tracking/sdlc/*/release/`
Destination: artifact
Owner: Overlay Maintainer
Status: open
Routed to:

### L-009 REQ-009 success counter increments before the transaction commits

Evidence: [p05 validation](../../../reviews/rpi/2026-09-16/order-cancellation-p05-validation.md) finding 3; [tests-2026-09-17.md](../evidence/tests-2026-09-17.md) Gaps REQ-009; release notes 1.0.0 Known Issues
Destination: intake
Owner: Product Owner
Status: open
Routed to:

### L-010 REQ-006 confirmation banner persistence across status refresh is undecided

Evidence: change record P07 (local update coexists with `OrdersRefreshOnStatusChange`); release notes 1.0.0 Known Issues
Destination: intake
Owner: UX Owner
Status: open
Routed to:

### L-011 REQ-002 unauthenticated path has no executed test because the functional host authenticates every request

Evidence: [tests-2026-09-17.md](../evidence/tests-2026-09-17.md) Gaps REQ-002; [p03 validation](../../../reviews/rpi/2026-09-16/order-cancellation-p03-validation.md)
Destination: intake
Owner: Test Engineer
Status: open
Routed to:

### L-012 Low-severity review items RV-002 and RV-005 remain open after release

Evidence: [review.md](../../../reviews/code-reviews/order-cancellation/review.md) RV-002 (full event payload logged in Catalog handler), RV-005 (unused NSubstitute references in WebApp.UnitTests)
Destination: intake
Owner: Tech Lead
Status: open
Routed to:

### L-013 Headless implementation and validation runs did not open audit sessions

Evidence: `session-audit metrics` shows one implement-stage session for eight phases; change records and validations exist without session ids
Destination: artifact
Owner: SDLC Conductor
Status: open
Routed to:

> AI-assisted content; review and validate before use.
