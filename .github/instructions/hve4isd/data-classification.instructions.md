---
description: "Data boundary rules for AI-assisted work in customer repositories: classification, redaction, and what may leave the tenant - Brought to you by ISD/hve4isd"
applyTo: '**'
---

# Data Classification Instructions

These rules apply to every file in a repository that carries the HVE4ISD overlay. They are intentionally short because they load with every request.

## Boundary

* Treat the repository's data classification as the one declared in `.copilot-tracking/sdlc/*/charter.md`. When no charter exists, assume `confidential`.
* Do not paste customer data, production identifiers, credentials, connection strings, private keys, or personal data into prompts, artifacts, commit messages, or work items. Use synthetic or redacted examples.
* Do not send repository content to services outside the approved MCP servers and model deployments configured for the engagement. Ask before fetching external URLs that would carry customer identifiers in the request.

## Redaction

* Replace names, emails, account numbers, and addresses with role-based placeholders such as `{{customer-admin}}` or `{{account-id}}` before writing them into `.copilot-tracking/` artifacts.
* Meeting transcripts and interview notes are redacted before any agent reads them.
* When a file must contain a real identifier for the work to be correct, note it in the artifact frontmatter with `contains_customer_data: true` so it is excluded from export.

## Model and Tool Selection

* Use the model deployments the engagement has approved. When a task needs a different model, state why and let the user choose.
* Prefer read-only tools until the user has confirmed scope. Destructive or externally visible actions (pushing, creating work items, deploying, sending messages) require explicit confirmation in the conversation.

## When Unsure

Stop and ask. A short clarification costs less than a data-handling incident.
