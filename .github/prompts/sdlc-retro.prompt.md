---
description: 'Run the learn stage: gather lifecycle evidence, draft a retrospective, and route each lesson to intake, an artifact change, or an eval case - Brought to you by ISD/hve4isd'
agent: SDLC Conductor
argument-hint: "project=... [since=YYYY-MM-DD]"
---

# SDLC Retro

## Inputs

* ${input:project}: (Required) Project slug under `.copilot-tracking/sdlc/`.
* ${input:since}: (Optional) Start date for the cycle under review. Defaults to the date of the last `release` gate approval, or the charter `created` date when no release exists.

## Requirements

1. Run Phase 1 and Phase 2 of the SDLC Conductor, then go directly to the learn-stage steps in Phase 4; skip the gate check.
2. Pass `since` to the audit store `metrics` command and cite the figures in the Evidence Summary.
3. Every lesson needs one destination and an owner role before the retrospective is presented as complete; list unrouted lessons explicitly.
4. Do not file work items, edit instructions, or add eval cases from this prompt; offer handoffs for confirmed lessons.
