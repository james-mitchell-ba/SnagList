# Testing, notifications, and non-functional requirements

## Testing

One test project per `src` project (see [02-solution-architecture.md](02-solution-architecture.md)):

- **`SnagList.Domain.Tests`** — invariants and the `SnagStatus` transition graph: every legal edge
  succeeds, every illegal edge (including every backward transition) is rejected.
- **`SnagList.Application.Tests`** — command/query handlers against fake ports, no database.
- **`SnagList.Infrastructure.Tests`** — repository, `IBlobStorage`, and email-sender behaviour
  against real Postgres and MinIO via Testcontainers, not mocks.
- **`SnagList.Api.Tests`** — an OpenAPI-contract test (the published document matches what the
  endpoints actually return), plus the CI-enforced REST/MCP parity test against the agent
  operation catalog.
- **`SnagList.Api.Auth.EntraId.Tests` / `SnagList.Api.Auth.Local.Tests`** — token validation and
  role-claim handling per provider.

End-to-end/smoke tests against the fully running local-docker stack are deferred to a hardening
backlog rather than built in v1, matching JointBooking's own deferral of that work.

## Notifications

`SnagReported` and `SnagStatusChanged` domain events drive `IEmailSender`:

- The reporter is emailed on every status change to their own `Snag`.
- The maintenance team is emailed, via a configured distribution address, on every new `Reported`
  `Snag` — this is also what gives `SnagSeverity = SafetyCritical` a concrete effect beyond a UI
  badge: it's the same notification path, just something maintenance should treat as more urgent
  when they see it land.

## Non-functional requirements

Single-tenant per deployment, internal-tool scale (expected: dozens of staff, low tens of `Snag`
reports a week). Deliberately light for v1:

- No caching layer — query volume doesn't warrant one.
- No rate limiting beyond standard authorization checks.
- No offline support in the Blazor WASM client.
- No formal accessibility compliance target beyond ordinary semantic HTML and Blazor's default
  component behaviour — revisit only if a real requirement surfaces.

If actual usage outgrows these assumptions, a fuller non-functional-requirements pass and a
hardening backlog (per JointBooking's own `09-hardening-backlog.md`) is the right next step, not a
reason to over-build this v1.

## Explicitly deferred (YAGNI for v1)

Per-person `Snag` assignment, a `Location` hierarchy beyond free-text sub-location, non-image
attachments, a reporting/analytics dashboard, multi-language UI, a mobile app.
