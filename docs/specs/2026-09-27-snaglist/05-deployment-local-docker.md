# Deployment: local docker

The root `docker-compose.yml` — a fully self-contained, disposable environment for development and
demos. Nothing here is shared with any other machine.

## Services

- **Keycloak** — its own throwaway container, `start-dev --import-realm`, hardcoded `admin`/`admin`
  credentials, importing `deploy/keycloak/realm-export.json` on startup. Nothing persists across a
  `down -v`; the realm is recreated from the checked-in export every time.
- **Postgres** — own container, published on the host for direct local access (e.g. `psql`,
  a GUI client) during development.
- **MinIO** + a bootstrap sidecar (`minio/mc`) that creates the photo bucket on startup.
- **Mailpit** — SMTP catcher + web UI, so the `SnagReported`/`SnagStatusChanged` notification
  emails are actually visible during local development without a real mail provider.
- **`api`, `mcp`, `web`** — the three app services, `Auth__Local__Authority` pointed at the
  in-compose Keycloak, all with published host ports.
- **`seed`** (compose profile, not started by default) — a one-shot service: runs EF Core
  migrations unconditionally, then an idempotent demo seed (a handful of `Location` rows and
  example `Snag` reports across the lifecycle), then Keycloak realm convergence.

## Local vs. home-lab

The only structural difference from [home-lab](06-deployment-home-lab.md) is Keycloak: local docker
runs its own throwaway instance; home-lab imports into a shared, already-running one. Everything
else (Postgres, MinIO, Mailpit, the three app services, the seed profile) is the same shape in
both, just with different published-port and credential conventions (local docker publishes host
ports and uses hardcoded local passwords for convenience; home-lab does not).
