# Deployment: home-lab

`deploy/home-lab/docker-compose.yml` — deployed to a shared home-lab host, not a disposable local
environment, but still meant to be fully re-creatable from what's checked into the repository.

## Identity: shared, external Keycloak

The home-lab already runs a shared Keycloak instance (owned outside this repository, the same way
JointBooking's home-lab deployment treats it). SnagList does not run its own Keycloak container
here — it ships a realm-export JSON that gets imported once into that shared instance by the
home-lab's own installer tooling, and points `Auth__Local__Authority` at it.

## Object storage: SnagList's own MinIO

Unlike Keycloak, MinIO is **not** shared — SnagList runs its own MinIO container plus a bootstrap
sidecar here, the same as it does in local docker. Each app on the home-lab gets its own bucket and
credentials rather than sharing a single MinIO instance across unrelated services.

## Other services

Own Postgres (internal-only network, unreachable from the host directly), own Mailpit, the `api`/
`mcp`/`web` app services, and the same one-shot `seed` profile as local docker.

## Networking

Two docker networks: an internal-only network for the database (and MinIO), and a network shared
with the home-lab's existing ingress for the parts that need to be reachable (`web`, and the `mcp`
endpoint if agents outside the compose stack need it).

## Disposability

A `--reseed` flag wipes the Postgres domain tables and the local MinIO bucket, and re-imports the
realm-export JSON into the shared Keycloak (recreating just SnagList's realm entries, not touching
the shared instance's other realms). Everything SnagList owns here is disposable-by-design; only
the shared Keycloak instance itself persists across a reseed.

## Images

Prebuilt images are pulled from the registry by release tag rather than built on the home-lab host
(`docker compose pull && up -d --no-build`), matching JointBooking's release process.
