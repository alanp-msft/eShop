---
description: "Enterprise conventions for .NET services that extend the HVE Core C# instructions: identity, secrets, observability, testing, and traceability - Brought to you by ISD/hve4isd"
applyTo: '**/*.cs, **/*.csproj, **/Directory.Build.props, **/Directory.Packages.props'
---

# .NET Enterprise Instructions

These conventions extend the HVE Core `csharp.instructions.md` and `csharp-tests.instructions.md`. Follow the HVE Core rules for language style; follow these rules for enterprise readiness. Target .NET 8 LTS or later.

## Solution Structure

* Use `Directory.Build.props` at the solution root to set `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, and `<AnalysisLevel>latest-recommended</AnalysisLevel>` once for every project.
* Use Central Package Management (`Directory.Packages.props`) so every project references the same package versions.
* Keep the layering `Api` (or `Web`) → `Application` → `Domain` ← `Infrastructure`; the domain project has no package references beyond the BCL.

## Identity and Secrets

* Authenticate to Azure resources with `DefaultAzureCredential` and managed identity; no connection strings with embedded keys in configuration.
* Protect APIs with the identity provider the charter records (Entra ID through `Microsoft.Identity.Web`, or the solution's existing OpenID Connect provider). Validate scopes or app roles with authorization policies, not manual claim checks in controllers.
* Read secrets from Key Vault through configuration providers. Never commit `appsettings.*.json` files that contain secrets; use user secrets locally.

## Observability

* Use `ILogger<T>` with structured message templates and the `LoggerMessage` source generator for hot paths. Never interpolate values into log strings.
* Add OpenTelemetry tracing, metrics, and logs exported to Azure Monitor. Propagate correlation through `Activity`.
* Expose `/healthz` (liveness) and `/readyz` (readiness) with `AddHealthChecks()`, including dependency checks for databases and downstream services.

## API Conventions

* Return `ProblemDetails` for errors and validate inputs at the boundary with data annotations or FluentValidation.
* Version APIs from the start (`Asp.Versioning`). Breaking changes require a new version and a deprecation note in the release notes.
* Make write operations idempotent where a client may retry; accept an idempotency key header for commands that create resources.

## Testing and Traceability

* Every requirement implemented in a change has at least one test whose display name starts with the requirement identifier. Use the project's existing test framework; do not introduce a second one:

```csharp
// xUnit
[Fact(DisplayName = "REQ-012 refund request with the same idempotency key returns the original result")]
public async Task Refund_WithDuplicateKey_ReturnsOriginal() { /* ... */ }

// MSTest 3+
[TestMethod("REQ-012 refund request with the same idempotency key returns the original result")]
public async Task Refund_WithDuplicateKey_ReturnsOriginal() { /* ... */ }

// NUnit
[Test(Description = "REQ-012 refund request with the same idempotency key returns the original result")]
public async Task Refund_WithDuplicateKey_ReturnsOriginal() { /* ... */ }
```

* Place a `// REQ-nnn` comment on the class or handler that implements the requirement so the requirement-trace skill can find it.
* Use `WebApplicationFactory<Program>` for API integration tests and Testcontainers or the solution's existing Aspire test host for real dependencies rather than mocking the data layer.
* Record test runs with `dotnet test` and the solution's configured runner (VSTest `--logger trx --collect:"XPlat Code Coverage"`, or Microsoft.Testing.Platform `--report-trx --coverage`) and reference the results in the evidence file for the `pr` gate.

## Patterns to Avoid

* `catch (Exception)` that swallows or only logs; rethrow or translate into a typed result.
* Static `HttpClient` construction; use `IHttpClientFactory` with resilience handlers.
* `DateTime.Now`; use `TimeProvider` so tests control time.
