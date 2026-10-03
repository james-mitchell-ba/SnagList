# Staff reporter appeal (option B) — design

Date: 2026-10-03. Status: proposed, awaiting review.
Scope: Staff reporters filing a `Snag` with minimum friction, plus visible
progress after filing. No domain-model change.

## Goal

A staff member on a phone files a `Snag` against a `Location` in one pass,
with photos, and afterwards can see that the report is moving without
chasing Maintenance. Success is time-to-file down and repeat reporting up,
measured by report counts and photo attach rate.

## Current state (verified in repo)

- `ReportSnag` is a two-step flow. The form at src/SnagList.Web/Pages/ReportSnag.razor
  posts a photo-less request, then photos are added afterwards on the detail
  page (src/SnagList.Web/Pages/SnagDetail.razor), which already offers both
  an upload picker and a mobile camera picker.
- The report form has no smart defaults: `Location` always starts at the
  first row, `SnagCategory` and `SnagSeverity` have no helper copy.
- The list at src/SnagList.Web/Pages/SnagList.razor shows every `Snag`
  with only a status filter; a reporter cannot isolate their own reports.
  The list query path (application query plus EF implementation under
  src/SnagList.Application/Snags/Queries and
  src/SnagList.Infrastructure/Persistence/Queries) has no reporter filter
  even though each row stores its reporter.
- Status mail already exists: src/SnagList.Application/Notifications/SnagNotificationDispatcher.cs
  emails the reporter on `SnagStatusChanged`, but the body is bare
  ("Snag {id} moved from X to Y") with no site context and no link back.

## Design

Four slices, shippable in order. Each keeps existing contracts unless noted.

### 1. Single-pass report with photos

The report form holds chosen image files in component state and, on submit,
first calls the existing create endpoint, then reuses the existing photo
upload endpoint once per file (at most 5, preserving the `Snag`
photo-cap invariant), then navigates to the new detail page. No API or
`SnagPhoto` contract change; the MCP surface is untouched. The form also
preselects the reporter's last-used `Location` from browser local storage,
and each of `SnagCategory` and `SnagSeverity` gets one line of plain-language
helper copy (for example, when to choose the safety-critical severity).

### 2. My-reports filter

Add an optional reporter filter to the list path: the application query gains the
reporter's staff id, the EF implementation applies it, GET /api/v1/snags accepts
an opt-in flag resolved server-side from the caller's own identity (never
from a client-supplied id), and the Blazor list gains a "My reports" toggle
next to the status dropdown. Cursor pagination behaviour is unchanged.

### 3. Enriched status-change mail

The dispatcher loads the `Snag` and its `Location` for context it already
has ids for, and sends: site name plus sub-location, previous and new
`SnagStatus`, and a deep link of the form {web-app-base-url}/snags/{id}.
A new web-app base URL setting joins the existing maintenance-team address
setting, configured per environment (local compose, home-lab, AWS). Payloads
of `SnagReported` and `SnagStatusChanged` are unchanged.

### 4. Reporter-facing status explainer

The detail page shows the lifecycle as a static step strip with the current
`SnagStatus` highlighted, alongside the existing `SnagComment` thread, so a
reporter sees where their report stands without knowing the transition graph.
No new endpoint; a full per-report history feed stays an explicit follow-up.

## Data flow

Report: form state (fields plus files) to create endpoint to per-photo
uploads to detail page. List: toggle to query flag to filtered cursor page.
Mail: domain event to dispatcher to context lookup to email with link.

## Error handling

- Partial photo failure (report created, some uploads fail): stay on the
  report flow with per-photo error state and a link to the created `Snag`
  instead of navigating blindly; the report itself is never lost.
- Failed reporter-identity lookup in mail dispatch keeps today's behaviour:
  no mail, no exception, no retry storm.
- Missing base-URL configuration fails startup validation, not silently
  producing link-less mail.

## Testing

- Dispatcher test: enriched subject, body contents, and link shape using the
  existing fake email sender and fake staff-identity repository.
- List test: multi-reporter seed, page through with a small limit, assert no
  cross-reporter leakage and stable cursors.
- Web tests in the existing style of tests/SnagList.Web.Tests/Pages/ReportSnagTests.cs:
  submit ordering (create before uploads before navigation) and toggle
  behaviour of the filter.
- Manual pass on a mobile viewport: file with two photos, confirm one
  navigation; acknowledge as Maintenance and inspect the mail for content
  and a working link.

## Non-goals

No change to the `Snag` transition graph, the 5-photo cap, `EditSnag` /
`WithdrawSnag` rules, per-person assignment, duplicate detection, drafts,
offline support, or a history endpoint. UI copy stays English-only per v1.

## Alternatives rejected

- Multipart create-with-photos endpoint: bigger contract and MCP parity
  surface for no extra capability at current volume.
- Client-side "my reports" filtering by reporter name: unreliable under
  cursor pagination.
- Enlarging the domain-event payloads with display strings: presentation
  detail does not belong in events; the dispatcher loads context instead.
