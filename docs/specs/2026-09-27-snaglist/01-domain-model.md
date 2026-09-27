# Domain model

The canonical, machine-checked source of these concepts is `docs/ontology.ttl` /
`docs/ontology.md`. This document explains the *why* behind the shapes recorded there; if the two
ever disagree, the ontology wins and this document is stale.

## Aggregate roots

### `Location`

A corporate site a `Snag` can be reported against — head office, northern office, engineering
site, and any added later. Admin-managed by `Maintenance`: `id`, `name`, `address`, `isActive`.
Retiring a `Location` (`isActive = false`) hides it from the picker for new reports; existing
`Snag` rows referencing it are untouched, so history stays intact even after a site closes.

### `Snag`

The thing the whole system exists to track: `id`, `locationId`, `subLocation` (free text, e.g.
"3rd floor, Room 3.12, near the kitchenette" — a full floor/room hierarchy is unwarranted for v1),
`category` (`SnagCategory`), `severity` (`SnagSeverity`), `description`, up to 5 `SnagPhoto`
attachments, `status` (`SnagStatus`), `reportedByStaffId`/`reportedByName` (mirrored at report
time), `reportedAt`, `version` (optimistic concurrency).

**Invariants:**
- Editable or withdrawable only while `status = Reported`. Once `Maintenance` has acknowledged it,
  the record is locked to further edits from the reporter — corrections from that point happen via
  a comment, not a silent rewrite of what was originally reported.
- `status` moves one-directionally: `Reported` → `Acknowledged` → `InProgress` → `Resolved` →
  `Closed`, with `Rejected` reachable only from `Reported` or `Acknowledged` (maintenance saying
  "not valid" or "duplicate") and `Withdrawn` reachable only from `Reported` (the reporter pulling
  it back). No transition moves backward — a `Snag` cannot un-resolve itself; if work turns out to
  be incomplete, a new `Snag` is filed.
- At most 5 photos.

## Entities

### `SnagComment`

A follow-up remark on a `Snag`, from either role, at any `status`: `id`, `snagId`,
`authorStaffId`, `authorName`, `body`, `createdAt`. This is the system's only unstructured
communication channel — "which floor exactly?", "parts ordered, ETA Friday" — without it, that
conversation would happen outside the tool and the history would be lost.

### `StaffIdentity`

A local mirror of an IdP-authenticated user: `staffId`, `name`, `email`, `roles`, `firstSeenAt`,
`lastSeenAt`. Written on first sight and refreshed opportunistically. It exists purely so the app
can render "Reported by Jane Smith" and attribute audit entries without re-querying the identity
provider on every page load — **it is never the authority for an access decision**. See
[Security & authentication](04-security-and-authentication.md).

## Value objects

### `SnagPhoto`

`blobKey`, `fileName`, `contentType`, `sizeBytes`, `uploadedAt`. Defined entirely by its values —
two photos with the same blob key, filename, and size are the same photo, no separate identity to
track.

## Enums

| Enum | Values | Notes |
|---|---|---|
| `SnagCategory` | `Electrical`, `Plumbing`, `StructuralOrFabric`, `HeatingAndCooling`, `CleaningAndHousekeeping`, `SafetyHazard`, `Other` | Lets `Maintenance` filter/triage by trade. |
| `SnagSeverity` | `Low`, `Medium`, `High`, `SafetyCritical` | Set by the reporter. `SafetyCritical` triggers the same-day maintenance-team notification (see [Testing & non-functional](08-testing-and-nonfunctional.md)). |
| `SnagStatus` | `Reported`, `Acknowledged`, `InProgress`, `Resolved`, `Closed`, `Rejected`, `Withdrawn` | See the `Snag` transition invariant above. |
| `StaffRole` | `Staff`, `Maintenance` | IdP-sourced role claim. Everyone who reports is `Staff`; `Maintenance` is the elevated, additional role — a maintenance team member can also file their own reports. |

## Domain events

- **`SnagReported`** — raised when a `Snag` is first filed. Payload: `snagId`, `locationId`,
  `severity`, `reportedByStaffId`, `reportedAt`. Drives the maintenance-team notification email.
- **`SnagStatusChanged`** — raised on every transition. Payload: `snagId`, `previousStatus`,
  `newStatus`, `changedByStaffId`, `changedAt`. Drives the reporter's notification email and the
  audit log entry.

## Use cases

`ReportSnag`, `EditSnag`, `WithdrawSnag` (all `Staff`, the reporter only, and the latter two only
while `Reported`); `AcknowledgeSnag`, `StartSnagWork`, `ResolveSnag`, `CloseSnag`, `RejectSnag`
(all `Maintenance`); `AddSnagComment` (either role); `CreateLocation`, `UpdateLocation`,
`RetireLocation` (all `Maintenance` — folded into the existing elevated role rather than standing
up a third `Admin` role, since locations change rarely).

## Relationships

`Snag` is reported at exactly one `Location`; a `Location` has many `Snag` reports. A `Snag` has
many `SnagComment` entries. A `Snag` is reported by one `StaffIdentity`; a `StaffIdentity` holds
one or more `StaffRole` values.

## Authorization shape (a deliberate simplification vs. JointBooking)

JointBooking needed a `StaffCapability` matrix because its roles carried a scoping dimension
(which appointment type a manager could act on). SnagList has no analogous scope — a `Maintenance`
member's authority doesn't vary per `Location` or `SnagCategory` — so authorization collapses to
two flat ASP.NET Core policies, `StaffPolicy` (either role) and `MaintenancePolicy` (`Maintenance`
only). Introducing the matrix indirection here would be complexity with nothing to protect against
yet; if per-location maintenance teams become a real requirement later, that's the point to
revisit this section.
