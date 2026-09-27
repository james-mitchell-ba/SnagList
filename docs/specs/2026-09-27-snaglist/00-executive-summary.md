# Executive summary

## Purpose

Authenticated staff at a corporate location — head office, northern office, engineering site, and
any site added later — can report a building-quality or maintenance issue (a `Snag`): what's
wrong, where, how urgent, optional photos. The maintenance team sees every report, triages it
through a fixed lifecycle, and resolves it. It is an internal tool, not a public-facing product:
single tenant, one deployment per organisation, low traffic (expected: dozens of staff, low tens
of `Snag` reports a week).

## Why three delivery surfaces

- A **Blazor WASM** web app is how staff and maintenance actually use the system day to day.
- The **REST API** underneath is the one thing everything else calls — HATEOAS-decorated so a
  client (human or automated) can discover valid next actions from a `Snag`'s current state rather
  than hardcoding the status-transition graph.
- An **MCP server** exposes the identical operations to AI agents, in lockstep with the REST API
  by construction (one operation catalog, one CI parity test) rather than as a hand-maintained
  second surface.

## Why three deployment patterns

The same container images run in all three; only configuration and the auth/storage backing
services differ:

| | Local docker | Home-lab | AWS |
|---|---|---|---|
| Identity provider | Throwaway in-compose Keycloak | Shared home-lab Keycloak (external) | Entra ID |
| Object storage | Own MinIO container | Own MinIO container | S3 |
| Compute | Docker Compose | Docker Compose | Lambda (container image) |
| Email | Mailpit | Mailpit | SES |

## Key decisions carried over from JointBooking

Clean Architecture layering with a ports-and-adapters boundary; cursor pagination and RFC 7807
errors everywhere; optimistic concurrency via a body-carried expected version rather than
`If-Match` headers; IdP-owned roles re-derived from the bearer token on every request rather than
trusted from a local cache; the home-lab-vs-local split (shared external identity provider vs. a
disposable in-compose one).

## Key decisions specific to SnagList

- No scope-matrix authorization (JointBooking needed one for its appointment-type scoping;
  SnagList's two roles need only two flat policies).
- A blob-storage port with a single S3-API-compatible implementation, since MinIO's entire purpose
  is speaking that API — JointBooking provisioned MinIO in compose but never built this port, so
  there was no existing code to reuse here.
- AWS compute is Lambda, packaged as the same container image used everywhere else (via the Lambda
  container-image runtime), not a separate build artifact.
- Photo uploads proxy through the API rather than using pre-signed direct-to-storage uploads, to
  avoid exposing storage credentials to the Blazor WASM client — acceptable given the low traffic
  and small file sizes involved.

## Explicitly out of scope for v1

Per-person `Snag` assignment (triage is team-wide), a location hierarchy beyond a free-text
sub-location, non-image attachments, a reporting/analytics dashboard, multi-language UI, a mobile
app, offline support.
