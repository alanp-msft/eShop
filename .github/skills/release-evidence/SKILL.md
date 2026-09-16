---
name: release-evidence
description: 'Builds a release manifest binding a version to its commit, gate approvals, and evidence hashes, drafts release notes and a rollback template, and verifies chain of custody afterwards - Brought to you by ISD/hve4isd'
---

# Release Evidence

## Overview

A release is justified by what was approved and what was verified. This skill writes `release/manifest-{version}.json` with the commit, the gate approval records, the newest test, trace, security, and work-item evidence, the change records, and each requirement's trace status, all with SHA-256 hashes. `complete: false` lists what is missing for the tier. It also drafts `release-notes-{version}.md` from that data and creates `rollback-{version}.md` from a template the first time only. The `release` gate requires the manifest and a rollback plan whose frontmatter a release manager has set to `status: ready`.

After shipping, `verify` re-hashes everything the manifest references and reports drift, so an auditor can confirm the approvals and evidence on disk are the ones that justified the release.

## Prerequisites

* Node.js 24 or later; no npm packages
* `git` on PATH for the commit and branch (recorded as `unknown` without it)
* A project with charter, requirements, and the evidence the tier requires

## Quick Start

```bash
node scripts/build-release.ts --project payments-api --version 1.4.0
node scripts/build-release.ts --project payments-api --version 1.4.0 verify
```

## Parameters Reference

| Parameter     | Required | Description                                          |
|---------------|----------|------------------------------------------------------|
| `--project`   | Yes      | Project slug                                         |
| `--version`   | Yes      | Release identifier, used in file names               |
| `--repo-root` | No       | Repository root; defaults to cwd                     |
| `--json`      | No       | `build` prints the manifest instead of the summary   |

Subcommands: `build` (default) and `verify`. Exit codes: 0 complete or intact, 1 incomplete or drifted, 2 usage error.

## What Complete Means

| Tier   | Gate approvals required | Evidence required                          |
|--------|-------------------------|--------------------------------------------|
| low    | pr                      | tests, trace                               |
| medium | design, plan, pr        | tests, trace, security (`pass`), workitems |
| high   | design, plan, pr, release | tests, trace, security (`pass`), workitems |

Every requirement must be `traced` in the newest trace evidence. Approval decisions must start with `approved`. The manifest is still written when incomplete so the missing list can drive work; the release gate reads only the newest manifest.

## Script Reference

```bash
node scripts/build-release.ts --project payments-api --version 1.4.0 --json | jq '.missing'
```

```powershell
node scripts/build-release.ts --project payments-api --version 1.4.0 verify; $LASTEXITCODE
```

## Agent Usage

1. Run `build` when the user asks to prepare a release. Present the missing list and route each item; do not fabricate evidence to clear it.
2. Edit the Summary and Known Issues sections of the drafted release notes from the change records and approval conditions. Leave the Requirements and Approvals tables as generated.
3. Never set `status: ready` on the rollback plan and never edit a manifest; the release manager reviews the plan, and `build` regenerates the manifest.
4. Run `verify` in post-release reviews and when the learn stage looks back at a cycle.

## Troubleshooting

| Symptom                                   | Cause and fix                                                                         |
|-------------------------------------------|---------------------------------------------------------------------------------------|
| `requirement REQ-nnn is untraced`         | Run the requirement-trace skill with `--write`; the manifest reads the newest trace   |
| `security evidence status is fail`        | Resolve or suppress findings through the security-evidence skill, then rebuild        |
| `commit: unknown`                         | `git` is not on PATH or the directory is not a repository                             |
| Release gate says rollback `status=draft` | The release manager has not reviewed the plan; they set `status: ready` in a reviewed change |
| `verify` reports drift after release      | An approval or evidence file changed after the manifest; treat as an audit finding    |

> Brought to you by ISD/hve4isd
