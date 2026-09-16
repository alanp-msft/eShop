---
description: "Enterprise conventions for Bicep infrastructure: Azure Verified Modules, identity, network posture by risk tier, tagging, and validation - Brought to you by ISD/hve4isd"
applyTo: '**/*.bicep, **/*.bicepparam'
---

# Bicep Enterprise Instructions

These conventions extend the HVE Core `bicep.instructions.md` with the controls customer landing zones expect. Follow the HVE Core rules for style and structure; follow these for enterprise posture.

## Modules and Structure

* Prefer [Azure Verified Modules](https://aka.ms/avm) (`br/public:avm/res/...`) over hand-written resources. Pin module versions explicitly.
* Organize as `infra/main.bicep` (subscription or resource-group scope) calling modules under `infra/modules/`, with one `.bicepparam` file per environment.
* Use `targetScope` explicitly and keep each deployment idempotent; no scripts that mutate state outside the template.

## Identity and Secrets

* Use system-assigned or user-assigned managed identities for every workload-to-Azure call; never emit keys or connection strings as outputs.
* Mark secret parameters `@secure()` and source them from Key Vault references in `.bicepparam` files (`getSecret`).
* Grant least-privilege RBAC with built-in roles scoped to the resource, not the subscription.

## Network and Data Posture by Risk Tier

| Risk tier | Required posture                                                                                          |
|-----------|-----------------------------------------------------------------------------------------------------------|
| low       | Public endpoints allowed with TLS 1.2 minimum and firewall rules; diagnostics enabled                     |
| medium    | Private endpoints for data services (SQL, Storage, Key Vault); `publicNetworkAccess: 'Disabled'` on them  |
| high      | Private endpoints for all PaaS services, customer-managed keys where supported, no public ingress except through WAF-fronted Front Door or Application Gateway |

Read the tier from the project charter and state it in a comment at the top of `main.bicep`.

## Tagging and Naming

* Apply these tags to every resource through a shared `tags` parameter: `project`, `environment`, `costCenter`, `owner`, `dataClassification`, `riskTier`.
* Follow the Cloud Adoption Framework abbreviations (`rg-`, `app-`, `sql-`, `kv-`, `st`) with `{workload}-{environment}-{region}` segments.

## Observability and Operations

* Enable diagnostic settings on every resource that supports them, routed to a shared Log Analytics workspace.
* Deploy Application Insights (workspace-based) for each application component and pass the connection string through configuration, not code.
* Add resource locks (`CanNotDelete`) to stateful resources in `prod` parameters.

## Validation and Traceability

* Run `bicep build` and `bicep lint` in CI, and `az deployment ... what-if` on pull requests against the target environment.
* Run PSRule for Azure (`PSRule.Rules.Azure`) with the Well-Architected baseline; treat failures as blocking for `medium` and `high` tiers.
* Add a `// REQ-nnn` comment above resources that satisfy a non-functional requirement so the requirement-trace skill can find them.

## Patterns to Avoid

* Hard-coded locations, subscription ids, or resource ids; derive them from parameters and `resourceGroup().location`.
* `listKeys()` in outputs or in application configuration.
* Wildcard CORS origins and `allowBlobPublicAccess: true` on storage accounts.
