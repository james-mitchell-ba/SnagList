# SnagList — Application Ontology

<!-- GENERATED FROM ontology.ttl. Edit that file, not this one. -->

> **AI instructions:** Read this file in full before starting any task that touches domain
> concepts. To change it, edit `ontology.ttl` and run `node scripts/build-ontology.mjs`. This
> file must never lag behind the code. See the Ontology protocol section in this repository's
> agent-instruction file.

Every domain concept is named here exactly once, as a backticked PascalCase term. Code, specs,
plans and prose use those names; `ontology.ttl` is the source, this file and they are the
consumers. A term that appears in Markdown but not here fails the deterministic check in
`scripts/check-ontology-terms.mjs`.

**Delete the sections that do not apply to this repository from `ontology.ttl`**, and remove the
matching names from `sections` in `ontology.config.json`. An empty section is worse than an
absent one — the optional semantic reviewer treats an empty required section as a configuration
error.
---

## Aggregate Roots

| Name | Description | Repository Interface |
| --- | --- | --- |
| `Location` | A corporate site staff can report a Snag against (e.g. head office, northern office, engineering site). Admin-managed by Maintenance. | ILocationRepository |
| `Snag` | A staff-reported building quality or maintenance issue at a Location, tracked from report through resolution. | ISnagRepository |

---

## Entities

| Name | Properties | Description |
| --- | --- | --- |
| `SnagComment` | `id`, `snagId`, `authorStaffId`, `authorName`, `body`, `createdAt` | A follow-up remark on a Snag, authored by the reporting staff member or Maintenance. |
| `StaffIdentity` | `staffId`, `name`, `email`, `roles`, `firstSeenAt`, `lastSeenAt` | A local mirror of an IdP-authenticated staff member, refreshed on first sight and opportunistically thereafter. Used for display and audit attribution only. |

---

## Value Objects

| Name | Properties | Description |
| --- | --- | --- |
| `SnagPhoto` | `blobKey`, `fileName`, `contentType`, `sizeBytes`, `uploadedAt` | A photo attached to a Snag at report time, addressed by its blob storage key. |

---

## Domain Events

| Name | Raising Aggregate | Payload Properties | Description |
| --- | --- | --- | --- |
| `SnagReported` | `Snag` | `snagId`, `locationId`, `severity`, `reportedByStaffId`, `reportedAt` | Raised when a staff member first reports a Snag. Drives the maintenance-team notification email. |
| `SnagStatusChanged` | `Snag` | `snagId`, `previousStatus`, `newStatus`, `changedByStaffId`, `changedAt` | Raised on every Snag status transition. Drives the reporter notification email and the audit log. |

---

## Enums

| Name | Values | Description |
| --- | --- | --- |
| `SnagCategory` | `Electrical`, `Plumbing`, `StructuralOrFabric`, `HeatingAndCooling`, `CleaningAndHousekeeping`, `SafetyHazard`, `Other` | The trade or discipline a Snag falls under, set by the reporting staff member. |
| `SnagSeverity` | `Low`, `Medium`, `High`, `SafetyCritical` | Urgency set by the reporting staff member; `SafetyCritical` triggers the same-day maintenance-team notification. |
| `SnagStatus` | `Reported`, `Acknowledged`, `InProgress`, `Resolved`, `Closed`, `Rejected`, `Withdrawn` | Lifecycle state of a Snag. See the `Snag` invariants for the allowed transition graph. |
| `StaffRole` | `Staff`, `Maintenance` | The IdP-sourced role claim that gates access. Every request's authorization decision is re-derived from this claim, never from the mirrored `StaffIdentity` row. |

---

## Use Cases

| Name | Actor | Description |
| --- | --- | --- |
| `ReportSnag` | Staff | A staff member files a new Snag against a Location, with an optional description, photos and severity. |
| `EditSnag` | Staff | The reporting staff member corrects their own Snag while it is still `Reported`. |
| `WithdrawSnag` | Staff | The reporting staff member pulls back their own Snag while it is still `Reported`, e.g. a mistaken or duplicate report. |
| `AcknowledgeSnag` | Maintenance | Maintenance confirms receipt of a `Reported` Snag. |
| `StartSnagWork` | Maintenance | Maintenance marks an `Acknowledged` Snag as `InProgress`. |
| `ResolveSnag` | Maintenance | Maintenance marks an `InProgress` Snag as `Resolved`. |
| `CloseSnag` | Maintenance | A `Resolved` Snag is closed out, ending its lifecycle. |
| `RejectSnag` | Maintenance | Maintenance marks a `Reported` or `Acknowledged` Snag as invalid or a duplicate, with a reason. |
| `AddSnagComment` | Staff or Maintenance | Staff or Maintenance adds a follow-up remark to a Snag, regardless of its current status. |
| `CreateLocation` | Maintenance | Maintenance adds a new corporate site that Snags can be reported against. |
| `UpdateLocation` | Maintenance | Maintenance edits an existing Location's name or address. |
| `RetireLocation` | Maintenance | Maintenance marks a Location `isActive = false`, hiding it from the picker for new Snags without deleting history. |

---

## Relationships

| From | Relationship | To | Cardinality |
| --- | --- | --- | --- |
| `Snag` | is reported at | `Location` | * → 1 |
| `Snag` | has | `SnagComment` | 1 → * |
| `Snag` | is reported by | `StaffIdentity` | * → 1 |
| `StaffIdentity` | holds | `StaffRole` | * → * |

---

## Business Rules & Invariants

- **`Location`** — must not be selectable for a new `Snag` once retired (`isActive = false`); existing `Snag` rows referencing it are unaffected.
- **`Snag`** — must not be edited or withdrawn once its `status` leaves `Reported`.
- **`Snag`** — `status` moves one-directionally along Reported → Acknowledged → InProgress → Resolved → Closed; `Rejected` is reachable only from `Reported` or `Acknowledged`, `Withdrawn` only from `Reported`; no transition may move backward.
- **`Snag`** — may carry at most 5 `SnagPhoto` attachments.
- **`StaffIdentity`** — is never the source of truth for authorization; every request's `Staff`/`Maintenance` access decision is re-derived from the bearer token's `roles` claim, not from this mirrored row.
