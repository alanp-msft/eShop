---
description: 'Check a lifecycle gate for a project, list missing artifacts, and record a human decision when the check passes - Brought to you by ISD/hve4isd'
agent: SDLC Conductor
argument-hint: "project=... gate={design|pr|release|production} [approve={true|false}]"
---

# SDLC Gate

## Inputs

* ${input:project}: (Required) Project slug under `.copilot-tracking/sdlc/`.
* ${input:gate}: (Required) Gate to check: `design`, `pr`, `release`, or `production`.
* ${input:approve:false}: (Optional, defaults to false) When true, and the check passes, ask the named approver for a decision and record it.

## Requirements

1. Run Phase 1 through Phase 3 of the SDLC Conductor for the given gate; skip Phase 4 unless the gate fails.
2. When `approve` is false, report the result and stop without recording anything.
3. When `approve` is true, require the approver's identity and decision to be stated in the conversation before recording.
