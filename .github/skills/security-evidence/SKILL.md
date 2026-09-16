---
name: security-evidence
description: 'Triages SARIF output from CodeQL, Microsoft Security DevOps, and other scanners into pr-gate security evidence with per-tier blocking thresholds and governed suppressions - Brought to you by ISD/hve4isd'
---

# Security Evidence

## Overview

Scanner findings only govern a release if a deterministic step reads them. This skill turns SARIF 2.1 output from any scanner into `evidence/security-{date}.md` whose frontmatter carries `status: pass` or `fail`. The `pr` gate requires the latest evidence to be `pass` for `medium` and `high` tiers, so blocking findings, an expired suppression, or a scanner that did not run all stop the merge.

Severity is normalized from the SARIF `security-severity` score when present (CodeQL, Trivy) and from the result level otherwise. What blocks depends on the charter's `risk_tier`, defined in [assets/severity-policy.json](assets/severity-policy.json):

| Tier   | Blocks at | Required tools | In-source suppressions |
|--------|-----------|----------------|------------------------|
| low    | high      | none           | honored                |
| medium | medium    | CodeQL         | honored                |
| high   | medium    | CodeQL         | ignored                |

Findings are accepted as false positives or accepted risk only through `.copilot-tracking/sdlc/{project}/security-suppressions.json` ([schema](assets/schemas/security-suppressions.schema.json)). Every entry needs a rule id, a justification of at least 20 characters, an approver, and an expiry no more than 90 days out. Expired or malformed entries fail the evidence rather than being ignored. CODEOWNERS routes that file to the security approver group.

## Prerequisites

* Node.js 24 or later; no npm packages
* A project charter with `risk_tier`
* SARIF files from your scanners. In CI the shipped pipelines run CodeQL and Microsoft Security DevOps; locally, download the `sarif-<run>` artifact or run the scanner yourself.

## Quick Start

```bash
node scripts/triage-sarif.ts --project payments-api --sarif ./sarif --write
```

Reads every `*.sarif` under `./sarif`, writes the evidence file, prints it, and exits 1 when the gate would fail.

## Parameters Reference

| Parameter        | Required | Default                                                   | Description                                       |
|------------------|----------|-----------------------------------------------------------|---------------------------------------------------|
| `--project`      | Yes      |                                                           | Project slug                                      |
| `--sarif`        | Yes      |                                                           | SARIF file or directory, repeatable, relative to `--repo-root` |
| `--repo-root`    | No       | cwd                                                       | Repository root                                   |
| `--suppressions` | No       | `.copilot-tracking/sdlc/{project}/security-suppressions.json` | Alternate suppressions file                  |
| `--policy`       | No       | `assets/severity-policy.json`                             | Alternate policy (customer-specific thresholds)   |
| `--write`        | No       | false                                                     | Persist `evidence/security-{date}.md`             |
| `--json`         | No       | false                                                     | Machine-readable findings and dispositions        |

## Script Reference

```bash
node scripts/triage-sarif.ts --project payments-api --sarif sarif/codeql --sarif sarif/msdo --json | jq '.findings[] | select(.disposition=="blocking")'
```

```powershell
node scripts/triage-sarif.ts --project payments-api --sarif sarif --json | ConvertFrom-Json | Select-Object -ExpandProperty findings | Where-Object disposition -eq blocking
```

Exit codes: 0 pass, 1 blocking findings or missing required tool or suppression problems, 2 usage error or missing charter.

## Suppression Example

```json
[
  {
    "rule": "cs/sql-injection",
    "path": "src/Payments/RefundRepository.cs",
    "justification": "Parameterized through Dapper; CodeQL does not model the sanitizer. Query rewrite tracked.",
    "approved_by": "{{security-champion}}",
    "expires": "2026-10-15",
    "work_item": "AB#1234"
  }
]
```

Omit `path` to suppress a rule everywhere; prefer a path so new occurrences still block.

## Agent Usage

1. When the pr gate reports `security-scan` missing or failing, read the latest `evidence/security-*.md` and present blocking findings grouped by rule with locations.
2. Propose fixes for real findings. For a suspected false positive, draft a suppression entry with the justification and expiry, but never write `security-suppressions.json`; the security approver adds it through a reviewed pull request.
3. Do not edit or delete evidence files, and do not re-run triage with a looser `--policy` to make the gate pass.

## Troubleshooting

| Symptom                                    | Cause and fix                                                                                       |
|--------------------------------------------|-----------------------------------------------------------------------------------------------------|
| `missing_tools: ["CodeQL"]`                | The scan job did not run or its SARIF was not downloaded; check the `scan` job and artifact name    |
| Finding shows `medium` but scanner said High | The scanner did not emit `security-severity`; the level mapping applies. Set `properties.severity` or adjust the policy |
| `expired` suppression problem              | Renew with a new approval and expiry, or fix the finding                                             |
| Gate says `status=fail, expected pass`     | Open the evidence file's Blocking Findings section; the gate reads only the newest dated file        |
| MSDO SARIF not found on Azure DevOps       | Confirm the task version's output directory and add it as another `--sarif` argument                 |

> Brought to you by ISD/hve4isd
