# Solution architecture

Clean Architecture, same layering discipline as JointBooking: dependencies point inward only.
`Domain` has no dependencies; `Application` depends only on `Domain` and defines the ports;
`Infrastructure` and the presentation projects (`Api`, `Mcp`, `Web`) depend on `Application` and
implement or call its ports — neither presentation project depends on the other.

## Projects

```
src/
  SnagList.Domain              — Snag, Location, SnagComment, StaffIdentity, SnagPhoto,
                                  enums, domain events, invariants. No dependencies.
  SnagList.Application          — commands, queries, and ports:
                                  ILocationRepository, ISnagRepository,
                                  IBlobStorage, IEmailSender, IClock, IAuditWriter
  SnagList.Infrastructure       — EF Core/Postgres repositories, the IBlobStorage adapter
                                  (single S3-API-compatible implementation — see below),
                                  the SMTP-based IEmailSender, the audit writer
  SnagList.Api                  — Minimal APIs, HATEOAS `_links`, OpenAPI + agent-friendly
                                  decoration (see 03-api-design.md)
  SnagList.Api.Auth.EntraId     — Entra ID bearer-JWT validation (AWS deployment)
  SnagList.Api.Auth.Local       — Keycloak bearer-JWT validation (home-lab + local docker)
  SnagList.Mcp                  — MCP server; thin adapter over the same Application handlers
  SnagList.Web                  — Blazor WASM standalone
  SnagList.SeedData             — EF migrations runner, demo seed, Keycloak realm convergence
tests/
  SnagList.<Project>.Tests      — one test project per src project
```

## Commands vs. queries

Commands (`ReportSnag`, `AcknowledgeSnag`, `AddSnagComment`, ...) load the aggregate through its
repository, apply the change, and persist — one transaction, invariants enforced by the aggregate
itself, optimistic concurrency via the `version` column. Queries (`ListSnags`, `GetSnag`,
`ListLocations`) bypass the aggregate entirely and run read-optimized SQL directly: there's nothing
to protect on a read, so there's no reason to pay for loading and reconstituting an aggregate just
to project it back out as a DTO.

No hot-contention path exists here (unlike JointBooking's slot-capacity counter, which needed an
atomic conditional `UPDATE`) — status changes happen at human pace, so plain optimistic
concurrency is sufficient everywhere.

## No scheduled work in v1

There's nothing analogous to JointBooking's invite-expiry sweep. If a future need arises (e.g. a
"this `Snag` has been `Reported` for 2 weeks with no acknowledgement" reminder), it becomes its own
entry point — a separate scheduled Lambda invocation or compose service — never an in-process
timer, since the AWS deployment's compute can be frozen between invocations.

## Blob storage: one port, one implementation

`IBlobStorage` lives in `Application`, with methods:

```
put(key, stream, contentType)      — store a photo
get(key)                           — stream a photo back
presignedGetUrl(key, expiry)       — for the photo-read redirect below
delete(key)
```

Its single implementation in `Infrastructure` uses the AWS S3 SDK against a configurable endpoint:
the real S3 endpoint in AWS, the in-compose MinIO endpoint everywhere else. MinIO's entire reason
for existing is S3-API compatibility, so a second, MinIO-specific implementation would be
duplication with nothing to show for it. (JointBooking never built this port at all — it
provisioned MinIO in compose but nothing in that codebase reads or writes to it — so there was no
prior implementation to reuse; this is new.)

## Auth stays split per provider, storage doesn't

Unlike blob storage, authentication genuinely differs enough between Entra ID and Keycloak
(different token issuers, different claim-mapping configuration, different admin APIs for realm
management) that JointBooking's `Api.Auth.EntraId` / `Api.Auth.Local` split is reused as-is. An
`Auth__Provider` configuration value selects which one wires up at startup.

## Photo upload flow

Blazor WASM posts `multipart/form-data` to `POST /api/v1/snags/{id}/photos`; the API streams the
bytes straight to `IBlobStorage` server-side. Reading a photo back
(`GET /api/v1/snags/{id}/photos/{photoId}`) 302-redirects to a short-lived pre-signed GET URL —
read access is low-risk enough to hand the client a direct link, whereas the write side stays
proxied through the API so storage credentials never reach the browser.

## AWS packaging

`SnagList.Api` and `SnagList.Mcp` each ship as a single container image with the AWS Lambda
container-image runtime wired in (`Amazon.Lambda.AspNetCoreServer.Hosting`). At startup, each
detects whether it's running under Lambda (`AWS_LAMBDA_FUNCTION_NAME` present) and either boots the
Lambda handler or Kestrel directly. One Dockerfile, one image, per service, across all three
deployment targets — only the entrypoint/hosting shim and the surrounding infrastructure differ.
