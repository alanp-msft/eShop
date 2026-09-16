---
description: "Layout, naming, frontmatter, and traceability rules for HVE4ISD lifecycle artifacts - Brought to you by ISD/hve4isd"
applyTo: '**/.copilot-tracking/sdlc/**'
---

# SDLC Artifact Instructions

Apply these conventions when creating or editing any file under `.copilot-tracking/sdlc/{project}/`. These artifacts are the durable contract between lifecycle stages, the inputs to human gates, and the evidence auditors read later.

## Project Folder Layout

```text
.copilot-tracking/sdlc/{project}/
├── charter.md              # Intake output; frontmatter validated by charter.schema.json
├── requirements.md         # ## REQ-nnn headings; single source of requirement identifiers
├── workitems.json          # REQ-nnn to tracker work item mapping; written only by the requirement-sync skill
├── state.json              # Current stage and gate status; sdlc-state.schema.json
├── gates/{gate}.json       # Human approval records written only by the sdlc-gate skill
├── security-suppressions.json  # Accepted scanner findings; owned by the security approver group
├── evidence/
│   ├── tests-{YYYY-MM-DD}.md   # Test Engineer run evidence
│   ├── trace-{YYYY-MM-DD}.md   # Requirement trace output
│   ├── workitems-{YYYY-MM-DD}.md # Work item sync state; frontmatter status synced|drift read by the design gate
│   └── security-{YYYY-MM-DD}.md # Scanner triage; frontmatter status pass|fail read by the pr gate
├── release/
│   ├── manifest-{version}.json     # Commit, gate approvals, evidence hashes; written only by the release-evidence skill
│   ├── release-notes-{version}.md
│   ├── rollback-{version}.md       # Template until the release manager sets status: ready
│   └── runbook-{version}.md
├── learning/
│   ├── retro-{YYYY-MM-DD}.md   # One per cycle; drafted by the SDLC Conductor
│   └── lessons.md              # Accumulating; entries are never deleted, only marked resolved
└── audit/sessions.db       # Session audit store; never edit by hand
```

The `{project}` slug is lowercase kebab-case and matches the `project` field in the charter frontmatter. Use the same slug in HVE Core artifacts (`{task_slug}` in RPI paths, `{project-slug}` in security and RAI plans) so gates can locate them.

Everything under `.copilot-tracking/sdlc/` except `audit/` is committed to the repository, as are RPI plans, changes, and reviews; CI gates read them. Research notes, sandboxes, session state, and the audit database stay local.

## Frontmatter

* Every markdown artifact starts with YAML frontmatter containing `title`, `description`, and `ms.date` in `YYYY-MM-DD` form.
* The charter additionally carries the fields required by `charter.schema.json`: `project`, `title`, `sponsor`, `risk_tier`, `data_classification`, `ai_components`, `platforms`, `stack`, `status`, `created`.
* Do not add an H1 heading; the frontmatter `title` is the document title. Start body content at H2.

## Requirement Identifiers

* Declare each requirement as a level-2 heading in `requirements.md`: `## REQ-012 Refund is idempotent`.
* Number sequentially with at least three digits. Never renumber or reuse an identifier; mark superseded requirements with a `Status: withdrawn` line instead.
* Under each heading include `Acceptance:` with testable criteria, `Priority:` (`must`, `should`, `could`), and `Source:` (PRD section, work item, or stakeholder).
* Cite the identifier in plan tasks, tests, source comments, change records, and reviews so the requirement-trace skill can build the matrix.
* Requirements are mirrored to the tracker named in the charter by the requirement-sync skill; edit `requirements.md`, never the work item, and re-run the sync. Withdraw with `Status: withdrawn` so the work item is closed rather than deleted.

## Risk Tier and Data Classification

* `risk_tier` is `low`, `medium`, or `high`. Raise the tier when any of these hold: the system handles `confidential` or `restricted` data, makes or shapes decisions about people, is customer-facing at scale, includes AI components with user-facing output, or touches payments, identity, or safety.
* `data_classification` is the highest classification of data the system stores or processes: `public`, `internal`, `confidential`, `restricted`.
* Set `ai_components: true` when the system calls or hosts a model; this makes the RAI plan a design-gate requirement.

## Gate Records

* Approval records are created only by the sdlc-gate skill after a human states a decision in the conversation.
* Never create, edit, or "fix" a `gates/*.json` file directly. If a record is wrong, the approver re-runs the recorder.
* When a gate is rejected, write the reasons into `state.json` under `gates` and route the work back to the producing stage.
* `state.json` advances past a gate only when that gate's record is `approved` or the gate does not apply to the tier: `implement` needs `gates/plan.json`, `release` needs `gates/pr.json`, `operate` needs `gates/release.json` (and `gates/production.json` for `high`). A plan critique disposition is an input to the `plan` gate, not a substitute for it.

## Evidence Files

* Evidence records what actually ran: command, exit code, counts, coverage figures, and the commit SHA. Do not summarize expected results as if they happened.
* Name files with the run date and keep prior runs; do not overwrite.
* Security evidence is written only by the security-evidence skill from scanner SARIF. Never hand-edit its `status`, and never add a suppression outside `security-suppressions.json`.
* End every AI-generated artifact with the line `> AI-assisted content; review and validate before use.`

## Lessons

* Declare each lesson in `learning/lessons.md` as a level-3 heading `### L-nnn Short statement`, numbered sequentially and never reused.
* Under each heading include `Evidence:` (links to the gate record, review finding, test evidence, trace, or metrics that support it), `Destination:` (`intake`, `artifact`, or `eval`), `Owner:` (a role, not a person), `Status:` (`open`, `routed`, `resolved`), and `Routed to:` (the work item, artifact path, or eval case once it exists).
* A lesson has exactly one destination. Split a lesson that needs two.
* Metrics quoted in retrospectives describe stages, agents, and artifacts. Do not attribute counts, durations, or outcomes to named individuals.

## Patterns to Avoid

* Placeholder artifacts created to satisfy a gate check.
* Copying requirement text into plans instead of citing the identifier.
* Storing secrets, tokens, connection strings, or customer personal data in any artifact under this folder.
