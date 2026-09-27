---
date: 2026-09-27
slug: add-snaglist-design-spec-and-implementation-plans
title: "Add SnagList design spec and implementation plans"
summary: "Reused from JointBooking, deliberately: Clean Architecture layering with a ports-and-adapters boundary; cursor pagination and RFC 7807 errors everywhere; optimistic concurrency via a body-carried expected version; hypermedia `_links` as…"
kind: product
status: accepted
sequence: 2026-09-27T09:18:24.000Z
evidence: "https://github.com/james-mitchell-ba/SnagList/pull/1; merge commit 61a2c4c9b276a425f94e9df70a139f5589641158"
---

## Context

The repo existed only as freshly-scaffolded baseline (AGENTS.md, Narrative, ontology kit, CI) with
no domain content. The user asked for a design spec and implementation plan for SnagList: staff
report building-quality/maintenance issues at a corporate site; a maintenance team triages and
resolves them; a Blazor WASM app, REST API, and MCP server all need to work off the same domain
model; the whole thing needs to run on local docker, a home-lab, and AWS. They pointed at the
JointBooking repo's deployment patterns as prior art to reuse rather than reinvent.

## Decision

Reused from JointBooking, deliberately: Clean Architecture layering with a ports-and-adapters
boundary; cursor pagination and RFC 7807 errors everywhere; optimistic concurrency via a
body-carried expected version; hypermedia `_links` as the actual mechanism a client (human or
agent) discovers valid next actions, not decoration, tied to one agent-operation-catalog kept in
CI-enforced parity with MCP; IdP-owned roles re-derived from the bearer token on every request,
never trusted from a local cache; the home-lab-vs-local-docker split (shared external identity
provider vs. a disposable in-compose one).

Decided differently for SnagList, and why: no `StaffCapability`-style scope matrix (JointBooking
needed one for per-appointment-type scoping; SnagList's two roles have no analogous scoping
dimension, so two flat authorization policies suffice) — a MinIO/S3-compatible blob storage port
gets one implementation used against both MinIO and real S3 (JointBooking never built this port at
all, despite provisioning MinIO in compose) — AWS compute is Lambda via container images, the same
images every other deployment uses, rather than a separate build artifact — home-lab's Keycloak
realm reconciliation runs against the Admin REST API (idempotent create/reseed) because that
instance is persistent and externally owned, unlike local docker's disposable one — a DB secret
resolves itself via Secrets Manager at Lambda startup rather than sitting in a plaintext
environment variable.

Per the `writing-plans` skill's own scope check, this became five separate plans rather than one:
each produces working, testable software on its own, and the combined scope (frontend + API + MCP
+ three deployment targets) was too large for one plan to stay bite-sized and placeholder-free.

Rejected: deferring notifications (the user chose to keep them in scope, which self-review later
caught as under-wired — fixed in Task 23 of Plan 1 by dispatching from the unit-of-work save, not by
threading a notifier through four command handlers). Rejected: ECS Fargate for AWS compute in favor
of the user's explicit choice of Lambda.

## Consequences

Five implementation plans exist and are self-reviewed (placeholder scans, spec-coverage checks,
type-consistency checks — each caught and fixed real issues before commit: a dangling UI field with
no form, a `with` expression against a sealed class, an uncertain Testcontainers API surface, a
`SnagStatusChanged` event missing the field its own consumer needed). None of the five has been
executed — no application code exists yet. Deliberately open: the actual home-lab hostname and AWS
Entra tenant/domain are placeholders throughout, since this repo doesn't own that infrastructure;
Plan 5's Task 9 is the only step in any plan that costs real money or needs a real AWS account, and
it hasn't been run. Follow-on work is executing these plans, most naturally starting with Plan 1.

---

AI-Fingerprint: sha256:e92e8c845462
