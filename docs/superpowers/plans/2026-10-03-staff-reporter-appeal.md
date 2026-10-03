# Staff reporter appeal (option B) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One-pass `Snag` reporting with photos for Staff, a personal report filter, and status-change mail Staff recognise.

**Architecture:** Client-orchestrated uploads over unchanged create/photo endpoints; one server-resolved reporter filter through the existing cursor-paged list; context-enriched mail from the existing dispatcher.

**Tech Stack:** .NET 10, Blazor WASM, EF Core with PostgreSQL, xUnit following existing test helpers.

**Spec:** docs/superpowers/specs/2026-10-03-staff-reporter-appeal-design.md

## Global Constraints

- Target net10.0 throughout; no new package dependencies.
- No new domain concepts: all prose reuses ontology terms; run the ontology term check after staging.
- The `Snag` lifecycle graph and the 5-photo cap are unchanged.
- Cursor pagination shape (`limit/cursor/nextCursor`) is unchanged.
- Copy stays English-only per v1 scope.

## Review Focus

- A photo larger than the 10 MB form limit is selected on the report form: reporter sees which file failed and the `Snag` link, nothing silently dropped.
- Partial upload failure (report created, 1 of 2 photos fails): flow stays put with per-photo errors, never navigates as if all succeeded.
- Two staff share a display name: the filter keys on staff id, so no leakage between same-named reporters.
- Reporter identity not yet mirrored when a status changes: mail is skipped quietly, as today.
- Stale row version on acknowledge during the manual check: the Maintenance action surfaces the conflict instead of silently overwriting.

## File Structure

- Modify: src/SnagList.Application/Notifications/SnagNotificationDispatcher.cs — loads report context, richer body plus deep link.
- Modify: src/SnagList.Application/Notifications/NotificationOptions.cs — adds the web-app base URL setting.
- Modify: src/SnagList.Application/Snags/Queries/ListSnagsQuery.cs — carries the optional reporter staff id.
- Modify: src/SnagList.Infrastructure/Persistence/Queries/EfSnagQueries.cs — applies the reporter filter.
- Modify: src/SnagList.Api/Endpoints/SnagEndpoints.cs — accepts the opt-in flag, resolves identity server-side.
- Modify: src/SnagList.Web/Services/SnagListFilter.cs — carries the toggle into the query string.
- Modify: src/SnagList.Web/Services/SnagListApiClient.cs — no signature change beyond the filter record.
- Modify: src/SnagList.Web/Pages/SnagList.razor — "My reports" toggle.
- Modify: src/SnagList.Web/Pages/ReportSnag.razor — file staging, ordered submit, last-location default, helper copy.
- Modify: src/SnagList.Web/Pages/SnagDetail.razor — static lifecycle strip.
- Tests sit beside existing suites: tests/SnagList.Application.Tests/Notifications/,
  list-query tests, and tests/SnagList.Web.Tests/Pages/.

### Task 1: Enriched status-change mail

**Files:**
- Modify: src/SnagList.Application/Notifications/SnagNotificationDispatcher.cs
- Modify: src/SnagList.Application/Notifications/NotificationOptions.cs
- Test: tests/SnagList.Application.Tests/Notifications/SnagNotificationDispatcherTests.cs

**Interfaces:**
- Consumes: existing staff-identity lookup and email sender fakes; report lookup by id.
- Produces: mail body containing site name, sub-location, previous and new `SnagStatus`, and detail link (used by Task 5 config).

- [ ] **Step 1: Extend the options with the base URL setting**

```csharp
public sealed class NotificationOptions
{
    public string MaintenanceTeamEmail { get; set; } = "";
    public string WebAppBaseUrl { get; set; } = "";
}
```

- [ ] **Step 2: Add failing tests for the enriched mail**

```csharp
[Fact]
public async Task SnagStatusChanged_mail_names_site_statuses_and_link()
{
    var emailSender = new FakeEmailSender();
    var staffIdentities = new FakeStaffIdentityRepository();
    staffIdentities.Add(StaffIdentity.FirstSeen("U1", "Jane Smith", "jane@example.com", [StaffRole.Staff], DateTimeOffset.UtcNow));
    var dispatcher = new SnagNotificationDispatcher(emailSender, staffIdentities,
        new NotificationOptions { MaintenanceTeamEmail = "maintenance@example.com", WebAppBaseUrl = "https://snaglist.example" });

    await dispatcher.DispatchAsync(
        [new SnagStatusChanged(Guid.NewGuid(), SnagStatus.Reported, SnagStatus.Acknowledged, "U9", "U1", DateTimeOffset.UtcNow)],
        default);

    var sent = Assert.Single(emailSender.SentEmails);
    Assert.Contains("Acknowledged", sent.Subject);
    Assert.Contains("https://snaglist.example/snags/", sent.Body);
}
```

- [ ] **Step 3: Run the new test to verify it fails**

Run: dotnet test tests/SnagList.Application.Tests --filter "FullyQualifiedName~SnagNotificationDispatcherTests"
Expected: FAIL on the new test (link missing from body).

- [ ] **Step 4: Implement context lookup plus enriched body in the dispatcher**

Load the `Snag` and its `Location` by the ids on the event, keep the
skip-quietly behaviour when the reporter or report is unknown, and compose
subject `Your Snag report changed status: {new}` with a body of site name,
sub-location, previous to new `SnagStatus`, and {base}/snags/{id} link.

- [ ] **Step 5: Run the suite to verify it passes**

Run: dotnet test tests/SnagList.Application.Tests --filter "FullyQualifiedName~SnagNotificationDispatcherTests"
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/SnagList.Application/Notifications tests/SnagList.Application.Tests/Notifications
git commit -m "feat: enrich reporter status-change mail with site and link"
```

### Task 2: Reporter filter on the list path

**Files:**
- Modify: src/SnagList.Application/Snags/Queries/ListSnagsQuery.cs
- Modify: src/SnagList.Infrastructure/Persistence/Queries/EfSnagQueries.cs
- Modify: src/SnagList.Api/Endpoints/SnagEndpoints.cs
- Test: list-query tests beside the existing application/infrastructure suites

**Interfaces:**
- Consumes: caller staff id from the request identity in the endpoint.
- Produces: filtered cursor pages honouring the existing `limit/cursor` shape (used by Task 3).

- [ ] **Step 1: Carry the optional reporter id on the query**

```csharp
public sealed record ListSnagsQuery(
    Guid? LocationId, SnagCategory? Category, SnagSeverity? Severity, SnagStatus? Status,
    string? ReportedByStaffId, string? Cursor, int Limit);
```

- [ ] **Step 2: Add a failing filter test**

Seed two reporters with interleaved timestamps, query with one staff id and
a small limit, assert every returned row belongs to that reporter and that
paging to the end yields the full per-reporter count with no duplicates.

- [ ] **Step 3: Run the new test to verify it fails**

Run: dotnet test tests/SnagList.Infrastructure.Tests --filter "FullyQualifiedName~EfSnagQueries"
Expected: FAIL (filter ignored, foreign rows returned).

- [ ] **Step 4: Apply the filter in the query and endpoint**

Add the reporter-staff-id predicate before cursor paging in the EF list
implementation, keeping ordering untouched; accept the opt-in flag on
GET /api/v1/snags and resolve the staff id server-side from the caller
identity, never from a client-supplied value.

- [ ] **Step 5: Run affected suites to verify green**

Run: dotnet test tests/SnagList.Application.Tests tests/SnagList.Infrastructure.Tests tests/SnagList.Api.Tests
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/SnagList.Application/Snags/Queries src/SnagList.Infrastructure/Persistence/Queries src/SnagList.Api/Endpoints tests
git commit -m "feat: filter snag list by reporting staff member"
```

### Task 3: Web list toggle and client threading

**Files:**
- Modify: src/SnagList.Web/Services/SnagListFilter.cs
- Modify: src/SnagList.Web/Services/SnagListApiClient.cs
- Modify: src/SnagList.Web/Pages/SnagList.razor
- Test: tests/SnagList.Web.Tests/Pages/SnagListTests.cs

**Interfaces:**
- Consumes: Task 2 flag on the API.
- Produces: working toggle state carried across reloads and load-more.

- [ ] **Step 1: Add the toggle to the filter record**

```csharp
public sealed record SnagListFilter(
    Guid? LocationId = null, SnagCategory? Category = null, SnagSeverity? Severity = null,
    SnagStatus? Status = null, bool OnlyMine = false, string? Cursor = null, int Limit = 20)
```

Thread it into the query string only when true; the API client needs no
other change.

- [ ] **Step 2: Add failing web tests for toggle behaviour**

Follow the existing style of the list tests: toggling on adds the flag to
the requested URL and resets the loaded items plus cursor; toggling off
removes it.

- [ ] **Step 3: Run the web tests to verify they fail**

Run: dotnet test tests/SnagList.Web.Tests --filter "FullyQualifiedName~SnagListTests"
Expected: FAIL on the new toggle tests.

- [ ] **Step 4: Implement the checkbox toggle in the list page**

Place the toggle beside the status dropdown, reset items and cursor on
change, and preserve it across load-more requests.

- [ ] **Step 5: Run the web suite to verify green**

Run: dotnet test tests/SnagList.Web.Tests
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/SnagList.Web/Services src/SnagList.Web/Pages/SnagList.razor tests/SnagList.Web.Tests/Pages/SnagListTests.cs
git commit -m "feat: add my-reports toggle to snag list"
```

### Task 4: Single-pass report form with photos

**Files:**
- Modify: src/SnagList.Web/Pages/ReportSnag.razor
- Test: tests/SnagList.Web.Tests/Pages/ReportSnagTests.cs

**Interfaces:**
- Consumes: existing create and per-photo upload client methods; Task 3 needs no input.
- Produces: created `Snag` id handed to the detail navigation only after uploads settle.

- [ ] **Step 1: Add failing tests for ordered submit**

Follow the existing report-page test style: with two staged files, submit
calls create once, then one upload per file, then navigates to the created
detail URL; when one upload fails, navigation does not happen and the page
shows a per-photo error with a link to the created report.

- [ ] **Step 2: Run the new tests to verify they fail**

Run: dotnet test tests/SnagList.Web.Tests --filter "FullyQualifiedName~ReportSnagTests"
Expected: FAIL (single-shot create with no uploads today).

- [ ] **Step 3: Stage files and submit in order**

Keep selected files in component state (reuse the camera and upload picker
pattern from the detail page), cap at 5, then on submit: create, upload each
file sequentially, and navigate only when all succeed. On any upload failure,
stay put, mark which photo failed, and link the created report.

- [ ] **Step 4: Add last-location default and helper copy**

Preselect the last-used `Location` from browser local storage (fallback to
first row), persist it after a successful report, and add one helper line
under each of the category and severity dropdowns.

- [ ] **Step 5: Run the web suite to verify green**

Run: dotnet test tests/SnagList.Web.Tests
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/SnagList.Web/Pages/ReportSnag.razor tests/SnagList.Web.Tests/Pages/ReportSnagTests.cs
git commit -m "feat: single-pass snag reporting with photos"
```

### Task 5: Status explainer, config, and docs

**Files:**
- Modify: src/SnagList.Web/Pages/SnagDetail.razor
- Modify: `docker-compose.yml`, deploy/home-lab/docker-compose.yml, deploy/aws/modules/config/main.tf — base URL setting per environment
- Modify: docs/user-guide/README.md (report flow plus mail contents)

**Interfaces:**
- Consumes: current `SnagStatus` already on the detail model; Task 1 setting value per environment.
- Produces: nothing downstream; closes the slice.

- [ ] **Step 1: Render the lifecycle strip**

Below the status badges, render the fixed order Reported, Acknowledged,
InProgress, Resolved, Closed with the current state highlighted and a note
that rejected or withdrawn reports leave the flow. No API change.

- [ ] **Step 2: Wire the base URL per environment**

Set the new base URL setting for local compose, home-lab, and AWS so the
Task 1 link points at the right web origin in each; fail startup when absent
rather than sending link-less mail.

- [ ] **Step 3: Update the user guide**

Document one-pass photo reporting, the personal filter, and what the mail
contains. Keep screenshots only if recaptured against the local deployment;
otherwise describe without images.

- [ ] **Step 4: Run full validation**

Run: dotnet test tests/SnagList.Application.Tests tests/SnagList.Infrastructure.Tests tests/SnagList.Api.Tests tests/SnagList.Web.Tests
Expected: PASS. Then stage the two new docs and run
node scripts/check-ontology-terms.mjs for the staged set.

- [ ] **Step 5: Commit**

```bash
git add src/SnagList.Web/Pages/SnagDetail.razor docs/user-guide/README.md docker-compose.yml deploy/home-lab/docker-compose.yml deploy/aws/modules/config
git commit -m "feat: reporter status explainer and guide"
```
