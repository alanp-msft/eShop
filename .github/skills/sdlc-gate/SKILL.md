---
name: sdlc-gate
description: 'Checks and records human SDLC gates (design, pr, release, production) by risk tier using artifact presence and signed approval records - Brought to you by ISD/hve4isd'
---

# SDLC Gate

## Overview

Gates are the control points of the HVE4ISD lifecycle. Each gate lists the artifacts that must exist before a human can approve it, and which gates apply depends on the project's `risk_tier` from its charter. This skill checks a gate, reports what is missing, and records a human decision with hashed evidence so audits can detect drift after approval.

Gate definitions live in [assets/gates.json](assets/gates.json). Artifact schemas live in [assets/schemas/](assets/schemas/). A requirement may carry a `frontmatter` object; the newest file matching its glob must then have those frontmatter values, which is how the `pr` gate requires the latest security evidence to say `status: pass`.

| Risk tier | Gates that apply                    |
|-----------|-------------------------------------|
| low       | pr, release                         |
| medium    | design, pr, release                 |
| high      | design, pr, release, production     |

## Prerequisites

* Node.js 24 or later. Scripts are TypeScript run directly by Node (native type stripping); no npm install, bundler, or compiler is needed.
* A project charter at `.copilot-tracking/sdlc/{project}/charter.md` with valid frontmatter. The charter parser supports scalars, inline lists, `- item` lists, and one level of nested mappings; keep the frontmatter to that subset.

## Quick Start

Check the design gate for project `payments-api` from the repository root:

```bash
node scripts/check-gate.ts --project payments-api --gate design
```

Record a human approval after the check passes:

```bash
node scripts/record-approval.ts --project payments-api --gate design \
  --decision approved --approved-by alice@contoso.com --session-id 5e4c40e51dc6 \
  --evidence charter=.copilot-tracking/sdlc/payments-api/charter.md \
  --evidence adr=docs/decisions/2026-09-15-api-style.md
```

Re-run the check with `--require-approval` to confirm the record is valid.

The `approval` field in the result is one of `absent`, `invalid` (record fails the schema or names another gate), `stale` (an approved evidence file changed or was removed since the decision), or the recorded decision. A stale approval fails the gate; the approver re-reviews and re-runs the recorder.

Scan lifecycle artifacts for secrets and personal identifiers before committing or exporting:

```bash
node scripts/scan-artifacts.ts
```

## Parameters Reference

### check-gate.ts

| Parameter            | Required | Default | Description                                              |
|----------------------|----------|---------|----------------------------------------------------------|
| `--project`          | Yes      |         | Project slug under `.copilot-tracking/sdlc/`             |
| `--gate`             | Yes      |         | `design`, `pr`, `release`, or `production`               |
| `--repo-root`        | No       | cwd     | Repository root used to resolve artifact globs           |
| `--require-approval` | No       | false   | Fail unless a valid `approved*` record exists            |
| `--json`             | No       | false   | Emit machine-readable output for CI and agents           |

### record-approval.ts

| Parameter       | Required | Description                                                     |
|-----------------|----------|-----------------------------------------------------------------|
| `--project`     | Yes      | Project slug                                                    |
| `--gate`        | Yes      | Gate name                                                       |
| `--decision`    | Yes      | `approved`, `approved_with_conditions`, or `rejected`           |
| `--approved-by` | Yes      | Human approver identity                                         |
| `--evidence`    | Yes      | One or more `artifact=path` entries; each file is SHA-256 hashed |
| `--session-id`  | Yes      | Audit session in which the evidence was presented and the decision stated; binds the approval to its context |
| `--condition`   | No       | Condition text, repeatable                                      |
| `--notes`       | No       | Free-form notes                                                 |

### scan-artifacts.ts

| Parameter        | Required | Default              | Description                                                                 |
|------------------|----------|----------------------|-----------------------------------------------------------------------------|
| paths            | No       | `.copilot-tracking`  | Files or directories to scan, relative to the repository root               |
| `--repo-root`    | No       | cwd                  | Repository root                                                             |
| `--allow-domain` | No       |                      | Email domain to treat as non-personal, repeatable (the customer's own domain is the usual entry) |
| `--json`         | No       | false                | Machine-readable findings                                                   |

Rules cover private keys, connection strings, cloud and API tokens, JWTs, SAS signatures, and email addresses. Sample text is redacted in output. `{{placeholder}}` tokens are ignored, `approved_by` in gate records is allowed, and files whose frontmatter declares `contains_customer_data: true` are listed as declared rather than failed. The scanner is scoped to lifecycle artifacts; run gitleaks or the repository host's secret scanning over the whole repository.

## Script Reference

Both scripts run identically from PowerShell, bash, VS Code terminals, and CI.

```powershell
node scripts/check-gate.ts --project payments-api --gate pr --json | ConvertFrom-Json
```

```bash
node scripts/check-gate.ts --project payments-api --gate pr --json | jq .missing
node scripts/scan-artifacts.ts --allow-domain customer.example --json | jq .findings
```

Exit codes: 0 pass or clean, 1 gate fails or findings present, 2 invalid invocation or malformed charter.

## Agent Usage

When an agent is asked whether work can proceed past a gate:

1. Run the check and read the JSON result.
2. If artifacts are missing, route the user to the agent or skill that produces each one instead of creating placeholders.
3. Never call `record-approval.ts` on the user's behalf. Present the passing check, ask the named approver role to decide, and run the recorder only after the human states a decision in the conversation.

## Troubleshooting

| Symptom                                   | Cause and fix                                                                     |
|-------------------------------------------|-----------------------------------------------------------------------------------|
| `Charter not found`                       | Run the Intake agent or `/sdlc-intake` to create the charter first                |
| `charter: $.risk_tier: ... not in [...]`  | Charter frontmatter has an invalid value; compare with `assets/schemas/charter.schema.json` |
| Gate reports `not required for this risk tier` | Expected; the tier does not include that gate. Raise the tier in the charter if the customer requires it |
| `approval: invalid`                       | The approval JSON was hand-edited; re-run `record-approval.ts`                    |
| `approval: stale`                         | An approved file changed after the decision; the approver re-reviews and re-records |
| Scan flags the sponsor's email in the charter | Pass `--allow-domain <customer-domain>`, or replace it with a role placeholder such as `{{sponsor}}` |
| `charter: $.platforms: expected object`   | Nested keys must be indented under `platforms:`; the parser handles one nesting level |
| `Unknown file extension ".ts"`            | Node is older than 24; upgrade. Node 22.13 to 23.x also works but prints experimental warnings |

> Brought to you by ISD/hve4isd
