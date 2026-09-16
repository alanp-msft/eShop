---
name: requirement-sync
description: 'Mirrors REQ-nnn requirements to Azure DevOps or GitHub work items with a hash-tracked mapping, drift detection, and design-gate evidence - Brought to you by ISD/hve4isd'
---

# Requirement Sync

## Overview

`requirements.md` is the source of truth; the tracker is a mirror. This skill diffs the `## REQ-nnn` headings against `.copilot-tracking/sdlc/{project}/workitems.json`, which records the work item id and a content hash per requirement from the last sync. Changed acceptance criteria, priority, or title show up as `update`; new headings as `create`; `Status: withdrawn` closes the item; a mapping entry with no heading is an `orphan` that a person resolves. `verify` writes `evidence/workitems-{date}.md` with `status: synced` or `drift`, and the `design` gate requires `synced` for `medium` and `high` tiers, so requirements cannot be approved for design while untracked.

Tracker access uses the customer's own CLI credentials (`gh` for GitHub Issues, `az boards` for Azure DevOps). Nothing in this skill stores tokens. Work items carry an `<!-- hve4isd:REQ-nnn -->` marker so they can be found later even if the mapping is lost.

## Prerequisites

* Node.js 24 or later; no npm packages
* Charter `platforms.tracker` set to `ado` or `github`, plus `tracker_org` and `tracker_project` (ADO) or `tracker_repo` as `owner/name` (GitHub)
* For `apply`: [GitHub CLI](https://cli.github.com/) authenticated with `gh auth login`, or [Azure CLI](https://learn.microsoft.com/cli/azure/) with the `azure-devops` extension and `az login`

## Quick Start

```bash
node scripts/sync-requirements.ts --project payments-api plan
node scripts/sync-requirements.ts --project payments-api apply --dry-run   # print the gh/az commands
node scripts/sync-requirements.ts --project payments-api apply             # run them and record ids
node scripts/sync-requirements.ts --project payments-api verify --write    # design-gate evidence
```

## Parameters Reference

| Parameter     | Applies to     | Description                                                   |
|---------------|----------------|---------------------------------------------------------------|
| `--project`   | all            | Project slug (required)                                       |
| `--repo-root` | all            | Repository root; defaults to cwd                              |
| `--json`      | `plan`         | Machine-readable plan                                         |
| `--dry-run`   | `apply`        | Print commands without running them                           |
| `--req`, `--id`, `--url` | `record` | Register a work item created by a person or an agent  |
| `--write`     | `verify`       | Persist `evidence/workitems-{date}.md`                        |

Exit codes: `plan` and `record` 0 on success; `apply` 1 when any command failed; `verify` 1 when status is `drift`; 2 for usage or charter errors.

## Script Reference

```bash
node scripts/sync-requirements.ts --project payments-api plan --json | jq '.plan[] | select(.action!="unchanged")'
```

```powershell
node scripts/sync-requirements.ts --project payments-api record --req REQ-007 --id 4718 --url https://dev.azure.com/contoso/Payments/_workitems/edit/4718
```

Work item types and fields are deliberately minimal (ADO `User Story` with title, description, area; GitHub issue with `requirement` label). Customers with richer templates can run `plan --json` and feed the HVE Core Backlog Manager agents instead, then `record` the resulting ids.

## Agent Usage

1. After `PRD Builder` or a requirements edit, run `plan` and show the user the pending actions.
2. Run `apply` only after the user confirms; creating or editing work items is an externally visible action. When the CLI is unavailable, show the `--dry-run` commands or hand off to the Backlog Manager agent and `record` the ids it reports.
3. Never edit `workitems.json` by hand and never resolve an `orphan` by deleting the mapping entry; ask whether the requirement was renamed (then `record` under the new id) or removed (then close the work item and remove the entry in a reviewed change).
4. Run `verify --write` before asking for the design gate.

## Troubleshooting

| Symptom                                           | Cause and fix                                                                    |
|---------------------------------------------------|----------------------------------------------------------------------------------|
| `tracker_org and tracker_project are required`    | Add `platforms.tracker_org` and `tracker_project` to the charter frontmatter     |
| `tracker_repo (owner/name) is required`           | Add `platforms.tracker_repo` for GitHub Issues                                   |
| `workitems.json tracker ... does not match charter` | The charter's tracker changed after items were created; migrate the ids first  |
| Every requirement shows `update` after a sync     | Frontmatter or field lines were reformatted; re-run `apply` once to refresh hashes |
| `az boards` says extension not installed          | `az extension add --name azure-devops`                                           |

> Brought to you by ISD/hve4isd
