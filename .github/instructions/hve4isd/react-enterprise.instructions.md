---
description: "Enterprise conventions for React and TypeScript front ends: authentication, accessibility, configuration, testing, and traceability - Brought to you by ISD/hve4isd"
applyTo: '**/*.ts, **/*.tsx'
---

# React Enterprise Instructions

Apply these conventions to React applications and shared TypeScript code in customer engagements. Target React 18 or later with TypeScript in `strict` mode and Vite as the default toolchain.

## Project Setup

* Enable `strict`, `noUncheckedIndexedAccess`, and `verbatimModuleSyntax` in `tsconfig.json`; use `import type` for type-only imports.
* Read runtime configuration from `import.meta.env.VITE_*` variables injected at build or deploy time. Never embed tenant identifiers, API keys, or secrets in source; anything in the bundle is public.
* Lint with ESLint (`typescript-eslint`, `react-hooks`, `jsx-a11y`) and format with Prettier; both run in CI.

## Authentication and Data Access

* Authenticate with `@azure/msal-react` against Entra ID using the authorization code flow with PKCE. Acquire tokens silently and scope them to the specific API.
* Call APIs through a single typed client module; components never build URLs or attach headers.
* Handle `401` by re-authenticating and `403` by rendering an access-denied state; do not retry silently.

## Accessibility

* Meet WCAG 2.2 AA. Every interactive element is keyboard reachable with a visible focus indicator; every image has meaningful `alt` text or `alt=""` when decorative.
* Use semantic elements and native controls before ARIA. When ARIA is required, follow the WAI-ARIA Authoring Practices pattern for that widget.
* Announce asynchronous results (saved, failed, loading) through live regions.
* Run `axe` in component tests for pages and complex components.

## State and Data

* Fetch server data with TanStack Query (or the customer's approved equivalent) and keep server state out of global stores.
* Validate API responses at the boundary with `zod` schemas generated or maintained alongside the API contract.
* Keep components presentational; move side effects and data access into hooks named `use{Feature}`.

## Testing and Traceability

* Test with Vitest and React Testing Library. Query by role and accessible name, not by test id, unless no accessible query exists.
* Start each test name for a requirement with its identifier:

```tsx
it("REQ-012 shows the refund confirmation after a successful submit", async () => {
  // ...
});
```

* Add a `// REQ-nnn` comment at the top of the component or hook that implements the requirement.
* Cover critical journeys with Playwright end-to-end tests and record the run in the `pr` gate evidence.

## Patterns to Avoid

* `any` and non-null assertions to silence the compiler.
* `useEffect` for derived state; compute during render or with `useMemo`.
* Storing tokens in `localStorage`; let MSAL manage the cache.
* `div` and `span` with click handlers standing in for buttons and links.
