# Security & authentication

## Claims

The identity provider supplies, as token claims: a staff identifier (custom claim, exact name and
format to be pinned down against the organisation's actual HR identifier convention during
implementation — do not invent a regex here), standard `name` and `email` claims, and a `roles`
claim carrying one or both of `Staff`/`Maintenance` (Entra ID App Roles in AWS, Keycloak realm
roles in home-lab/local).

## Authorization source of truth is the token

Every request re-derives `StaffPolicy`/`MaintenancePolicy` from the bearer JWT's `roles` claim,
checked on every authorized endpoint — not just `GET /api/v1/me`. This is default-deny: a token
with no recognised role claim gets no access at all, not even reporting. There is no anonymous or
possession-based access path in SnagList (unlike JointBooking's candidate token scheme) — every
caller authenticates.

## `StaffIdentity` is a cache, never an authority

On first sight of a valid token, the staff identifier/name/email/roles are mirrored into a
`StaffIdentity` row in Postgres, and refreshed opportunistically thereafter. This exists solely so
the app can render "Reported by Jane Smith" and attribute audit log entries without calling back to
the identity provider on every read. **It never gates an access decision** — a stale or unsynced
`StaffIdentity` row cannot grant or revoke anything; the token claims on the current request always
win.

## Auth is swappable per deployment, not per environment variable alone

`Auth__Provider` selects between `SnagList.Api.Auth.EntraId` (validates against Entra ID's
tenant-specific JWKS endpoint) and `SnagList.Api.Auth.Local` (validates against a Keycloak realm's
JWKS endpoint). Home-lab and local docker both use `.Auth.Local`, pointed at different Keycloak
instances (see the two deployment docs) — the code path is identical, only the issuer/JWKS
configuration differs.

## Audit log

Append-only; one row per mutating action (report, edit, withdraw, every status transition,
comment, location change); actor recorded as the staff identifier from the token, never a display
name that could later be edited out from under the record.

## Fail-fast startup

The app validates all required auth, storage, and email configuration at startup and refuses to
boot if anything is missing or left as a placeholder value — surfacing a misconfigured deployment
immediately, not on the first request that happens to touch the missing piece.
