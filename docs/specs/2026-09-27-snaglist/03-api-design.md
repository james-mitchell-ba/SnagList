# API design, HATEOAS, and MCP parity

## Resources

All under `/api/v1`, versioned by path.

| Resource | Operations | Notes |
|---|---|---|
| `locations` | list, get, create, update, retire | writes are `MaintenancePolicy`-only |
| `snags` | list (cursor-paginated, filterable by `locationId`/`category`/`severity`/`status`), get, create | create is `StaffPolicy` |
| `snags/{id}` | edit | reporter only, only while `Reported` |
| `snags/{id}/withdraw` | transition | reporter only, only while `Reported` |
| `snags/{id}/acknowledge`, `.../start`, `.../resolve`, `.../close`, `.../reject` | transitions | `MaintenancePolicy` only, one endpoint per lifecycle edge |
| `snags/{id}/comments` | post, list | either role |
| `snags/{id}/photos` | multipart upload | either role, capped at 5 per `Snag` |
| `snags/{id}/photos/{photoId}` | get | 302-redirect to a pre-signed GET URL |
| `me` | get | current identity + synced roles |

Plus `GET /api` (a root-relative link index) and `GET /openapi/v1.json` (the authoritative machine
contract).

## Hypermedia

Every response carries `_links`. Which transition links appear depends on both the resource's
current `status` and the caller's own authorization — a `Staff` caller reading their own `Reported`
`Snag` sees a `withdraw` link; a `Maintenance` caller reading the same resource sees `acknowledge`
and `reject` instead. This is the actual point of HATEOAS here, not decoration: a client — human or
agent — discovers valid next actions by following links instead of encoding the status-transition
graph from [01-domain-model.md](01-domain-model.md) into every caller.

The link record shape (following JointBooking's `ApiLink`): `{ href, method, operationId }`, keyed
by relation name in a `_links` dictionary, with `operationId` joining back to the OpenAPI document.

## Errors

RFC 7807 `problem+json`, with a stable `type` URI per failure mode clients can branch on:
`.../errors/invalid-status-transition`, `.../errors/version-conflict`,
`.../errors/photo-limit-exceeded`, etc. — never a bare message string a caller would have to parse.

## Concurrency

Every mutating request body carries `expectedVersion`. A mismatch returns 409 with the current
server-side representation of the resource, so the caller can decide how to reconcile rather than
silently retrying against stale data.

## Agent-friendly decoration and MCP parity

`GET /openapi/v1.json` is the authoritative contract: OpenAPI 3.1, annotated per operation with
`x-mcp-tool` (the corresponding MCP tool name) and `x-agent-hints` (natural-language usage hints).
One **agent operation catalog** is the single source of truth mapping each REST `operationId` to
its MCP tool; a CI test asserts every REST operation has a corresponding MCP tool and vice versa,
so the two surfaces cannot silently drift apart the way two independently-maintained API
descriptions would.

MCP tool coverage is full parity with REST, per the earlier design decision: report, edit,
withdraw, acknowledge, start, resolve, close, reject, comment, the three `Location` management
operations, and `GetMe`. Every tool enforces the same `StaffPolicy`/`MaintenancePolicy` as its REST
counterpart — there is no separate, more-permissive path through MCP. The server runs with
`Stateless = true` (no session affinity required behind a load balancer). Unlike JointBooking there
is no anonymous/candidate-token scheme to exclude from MCP: every SnagList caller is an
authenticated staff member, so the tool surface is uniformly role-gated, with nothing narrower to
carve out.
