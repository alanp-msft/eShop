---
description: 'Show the current lifecycle stage, gate status, and recommended next action for a project - Brought to you by ISD/hve4isd'
agent: SDLC Conductor
argument-hint: "[project=...]"
---

# SDLC Status

## Inputs

* ${input:project}: (Optional) Project slug. When omitted, list the projects under `.copilot-tracking/sdlc/` and ask which one to inspect.

## Requirements

1. Follow Phase 1 through Phase 4 of the SDLC Conductor in read-only mode: reconcile state, check the next gate, and recommend one next action.
2. Do not record approvals or draft release documents from this prompt.
3. Include the last three audit sessions for the project in the summary.
