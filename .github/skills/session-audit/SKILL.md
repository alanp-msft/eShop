---
name: session-audit
description: 'Records agent sessions, touched artifacts, and gate approvals in a per-customer SQLite audit store with JSON manifest export - Brought to you by ISD/hve4isd'
---

# Session Audit

## Overview

HVE Core acknowledges that multi-agent runs are not yet auditable in a structured way. This skill closes that gap for enterprise engagements. Each agent session opens a record, appends the artifacts it created, updated, or read (with SHA-256 hashes), links any gate approvals, and closes with an outcome. The store is a SQLite file inside the customer repository at `.copilot-tracking/sdlc/{project}/audit/sessions.db`, so nothing leaves the engagement boundary. Manifests export as JSON matching `session-manifest.schema.json` in the sdlc-gate skill for later ingestion into a central store when a customer permits it.

## Prerequisites

* Node.js 24 or later; the store uses the built-in `node:sqlite` module, so no npm packages are required. Node 22.13 to 23.x also works but prints an `ExperimentalWarning` for SQLite on every call; set `NODE_NO_WARNINGS=1` there.
* `.copilot-tracking/sdlc/*/audit/` present in the repository `.gitignore` so the database is never committed

## Quick Start

Open a session, record work, and close it:

```bash
SESSION=$(node scripts/session-audit.ts --project payments-api start --agent "Intake" --stage intake --model gpt-5)
node scripts/session-audit.ts --project payments-api artifact --session-id "$SESSION" --action created .copilot-tracking/sdlc/payments-api/charter.md
node scripts/session-audit.ts --project payments-api end --session-id "$SESSION" --outcome completed
```

Summarize or export:

```bash
node scripts/session-audit.ts --project payments-api report
node scripts/session-audit.ts --project payments-api export --output .copilot-tracking/sdlc/payments-api/audit/manifests.json
```

## Parameters Reference

Global options come before the subcommand.

| Option        | Required | Default | Description                     |
|---------------|----------|---------|---------------------------------|
| `--project`   | Yes      |         | Project slug                    |
| `--repo-root` | No       | cwd     | Repository root                 |

| Subcommand | Key arguments                                                                          | Purpose                                  |
|------------|----------------------------------------------------------------------------------------|------------------------------------------|
| `start`    | `--agent` (required), `--stage`, `--model`, `--host {vscode\|cli\|ci}`, `--operator`, `--agent-version` | Opens a session and prints its id       |
| `artifact` | `--session-id`, `--action {created\|updated\|read}`, one or more paths                 | Records touched files with hashes        |
| `approval` | `--session-id`, `--gate`, `--record-path`                                              | Links a gate approval to the session     |
| `end`      | `--session-id`, `--outcome {completed\|abandoned\|blocked}`                            | Closes the session                       |
| `export`   | `--session-id` (optional), `--output`                                                  | Emits JSON manifests                     |
| `report`   |                                                                                        | Per-agent, per-host session summary      |
| `metrics`  | `--since YYYY-MM-DD`, `--json`                                                         | Learning-stage input: sessions, outcomes, and average duration by stage and agent; rework hotspots; approvals by gate |

Host is auto-detected from `GITHUB_ACTIONS`, `TF_BUILD`, and `TERM_PROGRAM=vscode` when `--host` is omitted. Operator defaults to the OS user.

## Script Reference

```powershell
$session = node scripts/session-audit.ts --project payments-api start --agent "Test Engineer" --stage verify
node scripts/session-audit.ts --project payments-api artifact --session-id $session --action created tests/Payments.Tests/RefundTests.cs
node scripts/session-audit.ts --project payments-api end --session-id $session --outcome completed
```

## Agent Usage

Agents in the HVE4ISD overlay follow this contract:

1. Call `start` as the first action after confirming the project slug, and keep the returned id for the whole session.
2. Call `artifact` after each file the agent creates or materially updates; record `read` only for decision-shaping inputs such as charters, requirements, and plans.
3. Call `approval` when a human decision was recorded through the sdlc-gate skill during the session.
4. Call `end` before the final response, with `blocked` when a gate or missing input stopped progress.

Do not record secrets, credentials, or file contents; only paths and hashes are stored.

`metrics` aggregates by stage and agent and deliberately omits `operator`. Its output describes artifacts and workflows and must not be used to rate, rank, or evaluate individuals; this follows the boundary set in the HVE Core Transparency Note.

## CI Usage

The audit database is gitignored, so a CI run starts from an empty store. The shipped pipelines open a `CI Gate Runner` session per project and gate, record the charter and any approval record as `read`, close with `completed` or `blocked` from the gate result, and export the manifest with `export --session-id ... --output`. The manifests are published as a build artifact so auditors have a record of every gate evaluation that does not depend on an agent following its instructions. Host is detected as `ci` from `GITHUB_ACTIONS` or `TF_BUILD`.

## Troubleshooting

| Symptom                              | Cause and fix                                                                  |
|--------------------------------------|--------------------------------------------------------------------------------|
| `Unknown session`                    | The id was mistyped or belongs to another project; run `report` to list       |
| `database is locked`                 | Another process holds the store; retry, or close a stale VS Code terminal      |
| `Cannot find module 'node:sqlite'`   | Node is older than 22.13; upgrade to 24                                        |
| Database appears in `git status`     | Add `.copilot-tracking/sdlc/*/audit/` to `.gitignore`                          |
| `sha256` is null in export           | The path did not exist when recorded; record after the file is written        |

> Brought to you by ISD/hve4isd
