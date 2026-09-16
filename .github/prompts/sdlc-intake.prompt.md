---
description: 'Start intake for a new project and produce its charter, risk tier, and gate plan - Brought to you by ISD/hve4isd'
agent: Intake
argument-hint: "project=... [idea=...] [tracker={ado|github}] [repoHost={ado|github}]"
---

# SDLC Intake

## Inputs

* ${input:project}: (Required) Lowercase kebab-case project slug, for example `payments-api`.
* ${input:idea}: (Optional) One-paragraph description of the problem or request. Inferred from the conversation when omitted.
* ${input:tracker:ado}: (Optional, defaults to ado) Work tracker, `ado` or `github`.
* ${input:repoHost:ado}: (Optional, defaults to ado) Repository host, `ado` or `github`.

## Requirements

1. Use the supplied slug for the charter folder and confirm it before writing files.
2. Treat `tracker` and `repoHost` as defaults the user can override in Phase 1.
3. Default the stack to .NET API, React front end, and Bicep infrastructure unless the user states otherwise.
