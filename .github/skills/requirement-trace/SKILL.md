---
name: requirement-trace
description: 'Builds a REQ-nnn traceability matrix across plans, source, tests, change records, and reviews, and fails on orphan requirements - Brought to you by ISD/hve4isd'
---

# Requirement Trace

## Overview

Traceability is the thread that turns separate HVE Core artifacts into an auditable lifecycle. Requirements are declared once as `## REQ-nnn` headings in `.copilot-tracking/sdlc/{project}/requirements.md`. Plans, tests, source comments, change records, and reviews cite the identifier. This skill scans the repository, builds the matrix, and reports each requirement as `traced` (plan, source, and tests), `unplanned` (source and tests but no plan task cites it), `untested` (source only), or `unimplemented`. `--fail-on-orphans` fails on anything but `traced`.

## Prerequisites

* Node.js 24 or later; no npm packages are required
* A `requirements.md` for the project with at least one `## REQ-nnn` heading

## Quick Start

```bash
node scripts/trace-requirements.ts --project payments-api --write
```

Writes `.copilot-tracking/sdlc/payments-api/evidence/trace-YYYY-MM-DD.md`, which the `pr` gate requires. Add `--fail-on-orphans` in CI.

## Parameters Reference

| Parameter           | Required | Default                                   | Description                                        |
|---------------------|----------|-------------------------------------------|----------------------------------------------------|
| `--project`         | Yes      |                                           | Project slug                                       |
| `--repo-root`       | No       | cwd                                       | Repository root                                    |
| `--requirements`    | No       | `.copilot-tracking/sdlc/{project}/requirements.md` | Alternate requirements file              |
| `--scan`            | No       | `src tests docs .copilot-tracking`        | Directories to scan for references                 |
| `--fail-on-orphans` | No       | false                                     | Exit 1 when any requirement lacks source or tests  |
| `--write`           | No       | false                                     | Persist the matrix as dated evidence               |
| `--json`            | No       | false                                     | Emit the raw reference map                         |

## Citation Conventions

Agents and engineers cite requirements so the scanner can find them:

| Location          | Convention                                                        |
|-------------------|-------------------------------------------------------------------|
| RPI plan task     | Include `REQ-012` in the task `Requirements:` block               |
| .NET test         | `[Fact(DisplayName = "REQ-012 ...")]`, `[TestMethod("REQ-012 ...")]`, or `[Test(Description = "REQ-012 ...")]` |
| React test        | `it("REQ-012 shows refund confirmation", ...)`                    |
| Source            | A one-line comment at the class or component: `// REQ-012`        |
| Bicep             | `// REQ-030` above the resource                                   |
| Change record     | Reference the identifier in the change summary                    |

Files under `tests/`, `__tests__/`, `*.test.*`, `*.spec.*`, and `*Tests.cs` count as tests. `.cs`, `.ts`, `.tsx`, `.js`, `.jsx`, `.bicep`, and `.sql` outside those paths count as source.

## Script Reference

```powershell
node scripts/trace-requirements.ts --project payments-api --json | ConvertFrom-Json
```

```bash
node scripts/trace-requirements.ts --project payments-api --fail-on-orphans
```

Exit codes: 0 all requirements traced (or orphan failure not requested), 1 orphans present with `--fail-on-orphans`, 2 requirements file missing or empty.

## Troubleshooting

| Symptom                                  | Cause and fix                                                                    |
|------------------------------------------|----------------------------------------------------------------------------------|
| `No ## REQ-nnn headings found`           | Requirements use another format; convert each to a level-2 heading starting with the id |
| A test is counted as source              | Move it under `tests/` or name it `*.test.*`, `*.spec.*`, or `*Tests.cs`         |
| A monorepo folder is not scanned         | Pass `--scan` with the additional roots                                          |

> Brought to you by ISD/hve4isd
