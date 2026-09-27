---
date: 2026-09-27
slug: fix-local-docker-wire-up-auth-end-to-end-add-user-guide-with-screenshots
title: "fix(local-docker): wire up auth end-to-end, add user guide with screenshots"
summary: "Fixed each gap at the layer it belongs to rather than working around it in the app: added the missing script tag Blazor's own tooling expects, moved the custom claim mappers to the shared client scope so they reach every client that…"
kind: product
status: accepted
sequence: 2026-09-27T19:01:44.000Z
evidence: "https://github.com/james-mitchell-ba/SnagList/pull/7; merge commit 6bb652f458e419277e890fcc960c03516fd565ec"
---

## Context

The local Docker deployment (`docs/specs/2026-09-27-snaglist/05-deployment-local-docker.md`) had
never been exercised end-to-end — sign-in itself was broken, so nothing behind it had been
verified either. Producing real, working screenshots for a user guide required getting the whole
stack (Blazor auth, Keycloak, CORS) actually functional for the first time.

## Decision

Fixed each gap at the layer it belongs to rather than working around it in the app: added the
missing script tag Blazor's own tooling expects, moved the custom claim mappers to the shared
client scope so they reach every client that requests it (not just `snaglist-api` itself), and
added a conventional `Cors:AllowedOrigins` config section to the API rather than hardcoding the
web origin. Rejected: patching around the broken auth flow in the Blazor app instead of fixing the
Keycloak realm config — rejected because the realm export is the actual source of truth and other
deployments (home-lab) will hit the same gap.

## Consequences

Local Docker sign-in, Snag reporting/commenting, and Maintenance triage/Location management now
work end-to-end and are documented with real screenshots. Left open: the Blazor WASM client's own
`ClaimsPrincipal` still doesn't surface the `roles`/`staff_id` claims client-side (the nav bar shows
"Jane Smith ()" instead of "(Staff)") — purely cosmetic, since all real authorization already
happens server-side against the JWT bearer token, so it's tracked separately rather than folded
into this fix.

---

AI-Fingerprint: sha256:9315bb3cbd3f

🤖 Generated with [Claude Code](https://claude.com/claude-code)
