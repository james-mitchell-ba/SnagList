# SnagList Core Domain & API Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A working, testable REST API — `Location`/`Snag` domain, Clean Architecture layering, Postgres persistence, S3-compatible blob storage, Keycloak auth — runnable end-to-end via the local docker-compose stack, with no frontend yet.

**Architecture:** `SnagList.Domain` (no deps) → `SnagList.Application` (commands/queries + ports) → `SnagList.Infrastructure` (EF Core/Postgres, blob storage, email, audit) and `SnagList.Api` (Minimal APIs, HATEOAS, `SnagList.Api.Auth.Local` for Keycloak JWT). See [02-solution-architecture.md](02-solution-architecture.md).

**Tech Stack:** .NET 10 (`net10.0`), ASP.NET Core Minimal APIs, EF Core + Npgsql, AWSSDK.S3 (against MinIO), MailKit, xUnit, Testcontainers (Postgres + MinIO).

**Spec:** [docs/specs/2026-09-27-snaglist/](README.md) — this plan implements 01-domain-model.md, 02-solution-architecture.md, 03-api-design.md (REST surface only; MCP parity is a later plan), 04-security-and-authentication.md (Local/Keycloak side only; Entra ID is the AWS-deployment plan), and 05-deployment-local-docker.md.

## Global Constraints

- `net10.0`, nullable enabled, implicit usings enabled (repo-wide `Directory.Build.props`, Task 1).
- Cursor pagination only — never offset (per the user's global instruction and 03-api-design.md).
- Every mutating command takes an `expectedVersion`; a mismatch is a 409 with the current server state, never a silent overwrite.
- RFC 7807 `problem+json` for every error response, with a stable `type` URI.
- `IBlobStorage` has exactly one implementation (S3-API-compatible), used against both real S3 and MinIO — no per-target duplication (see 02-solution-architecture.md).
- Authorization is re-derived from the bearer token's `roles` claim on every request; `StaffIdentity` is a display/audit cache only, never consulted for an access decision.
- No placeholder code, no `TODO`s left in committed code — every task ships working, tested code.

---

## Task 1: Solution & project scaffolding

**Files:**
- Create: `SnagList.sln`
- Create: `Directory.Build.props`
- Create: `src/SnagList.Domain/SnagList.Domain.csproj`
- Create: `src/SnagList.Application/SnagList.Application.csproj`
- Create: `src/SnagList.Infrastructure/SnagList.Infrastructure.csproj`
- Create: `src/SnagList.Api/SnagList.Api.csproj`
- Create: `src/SnagList.Api.Auth.Local/SnagList.Api.Auth.Local.csproj`
- Create: `src/SnagList.SeedData/SnagList.SeedData.csproj`
- Create: `tests/SnagList.Domain.Tests/SnagList.Domain.Tests.csproj`
- Create: `tests/SnagList.Application.Tests/SnagList.Application.Tests.csproj`
- Create: `tests/SnagList.Infrastructure.Tests/SnagList.Infrastructure.Tests.csproj`
- Create: `tests/SnagList.Api.Tests/SnagList.Api.Tests.csproj`

**Interfaces:**
- Produces: the project graph every later task builds inside. No code interfaces yet.

- [ ] **Step 1: Create `Directory.Build.props` at the repo root**

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Create the solution and class library projects**

```bash
dotnet new sln -n SnagList

dotnet new classlib -o src/SnagList.Domain -n SnagList.Domain
dotnet new classlib -o src/SnagList.Application -n SnagList.Application
dotnet new classlib -o src/SnagList.Infrastructure -n SnagList.Infrastructure
dotnet new classlib -o src/SnagList.Api.Auth.Local -n SnagList.Api.Auth.Local
dotnet new web -o src/SnagList.Api -n SnagList.Api
dotnet new console -o src/SnagList.SeedData -n SnagList.SeedData

dotnet new xunit -o tests/SnagList.Domain.Tests -n SnagList.Domain.Tests
dotnet new xunit -o tests/SnagList.Application.Tests -n SnagList.Application.Tests
dotnet new xunit -o tests/SnagList.Infrastructure.Tests -n SnagList.Infrastructure.Tests
dotnet new xunit -o tests/SnagList.Api.Tests -n SnagList.Api.Tests
```

Delete the template-generated `Class1.cs` / `UnitTest1.cs` / `Program.cs` sample content from each — every file that survives this task is one a later task writes deliberately.

- [ ] **Step 3: Wire project references**

```bash
dotnet sln add src/SnagList.Domain src/SnagList.Application src/SnagList.Infrastructure \
  src/SnagList.Api src/SnagList.Api.Auth.Local src/SnagList.SeedData \
  tests/SnagList.Domain.Tests tests/SnagList.Application.Tests \
  tests/SnagList.Infrastructure.Tests tests/SnagList.Api.Tests

dotnet add src/SnagList.Application reference src/SnagList.Domain
dotnet add src/SnagList.Infrastructure reference src/SnagList.Application
dotnet add src/SnagList.Api reference src/SnagList.Application
dotnet add src/SnagList.Api reference src/SnagList.Infrastructure
dotnet add src/SnagList.Api reference src/SnagList.Api.Auth.Local
dotnet add src/SnagList.Api.Auth.Local reference src/SnagList.Application
dotnet add src/SnagList.SeedData reference src/SnagList.Infrastructure

dotnet add tests/SnagList.Domain.Tests reference src/SnagList.Domain
dotnet add tests/SnagList.Application.Tests reference src/SnagList.Application
dotnet add tests/SnagList.Infrastructure.Tests reference src/SnagList.Infrastructure
dotnet add tests/SnagList.Api.Tests reference src/SnagList.Api
```

- [ ] **Step 4: Build to confirm the graph is sound**

Run: `dotnet build`
Expected: succeeds (empty projects, no code yet).

- [ ] **Step 5: Commit**

```bash
git add Directory.Build.props SnagList.sln src tests
git commit -m "chore: scaffold solution and project references"
```

---

## Task 2: Domain — `Location` aggregate

**Files:**
- Create: `src/SnagList.Domain/Locations/Location.cs`
- Test: `tests/SnagList.Domain.Tests/Locations/LocationTests.cs`

**Interfaces:**
- Produces: `Location.Create(name, address) -> Location`, `location.Update(name, address)`, `location.Retire()`, properties `Id`, `Name`, `Address`, `IsActive`.

- [ ] **Step 1: Write the failing tests**

```csharp
namespace SnagList.Domain.Tests.Locations;

using SnagList.Domain.Locations;
using Xunit;

public class LocationTests
{
    [Fact]
    public void Create_starts_active()
    {
        var location = Location.Create("Head Office", "1 Main St");

        Assert.True(location.IsActive);
        Assert.Equal("Head Office", location.Name);
        Assert.Equal("1 Main St", location.Address);
        Assert.NotEqual(Guid.Empty, location.Id);
    }

    [Fact]
    public void Update_changes_name_and_address()
    {
        var location = Location.Create("Head Office", "1 Main St");

        location.Update("Head Office (renamed)", "2 Main St");

        Assert.Equal("Head Office (renamed)", location.Name);
        Assert.Equal("2 Main St", location.Address);
    }

    [Fact]
    public void Retire_sets_IsActive_false()
    {
        var location = Location.Create("Head Office", "1 Main St");

        location.Retire();

        Assert.False(location.IsActive);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/SnagList.Domain.Tests --filter LocationTests`
Expected: FAIL — `Location` does not exist.

- [ ] **Step 3: Implement `Location`**

```csharp
namespace SnagList.Domain.Locations;

public sealed class Location
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public string Address { get; private set; } = "";
    public bool IsActive { get; private set; }

    private Location() { } // EF Core

    public static Location Create(string name, string address) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Address = address,
        IsActive = true,
    };

    public void Update(string name, string address)
    {
        Name = name;
        Address = address;
    }

    public void Retire() => IsActive = false;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/SnagList.Domain.Tests --filter LocationTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/SnagList.Domain/Locations tests/SnagList.Domain.Tests/Locations
git commit -m "feat(domain): add Location aggregate"
```

---

## Task 3: Domain — `Snag` aggregate and the `SnagStatus` transition graph

This is the most important piece of domain logic in the system: every legal and illegal edge of
the lifecycle graph from [01-domain-model.md](01-domain-model.md) must be exercised.

**Files:**
- Create: `src/SnagList.Domain/Snags/SnagCategory.cs`
- Create: `src/SnagList.Domain/Snags/SnagSeverity.cs`
- Create: `src/SnagList.Domain/Snags/SnagStatus.cs`
- Create: `src/SnagList.Domain/Snags/InvalidSnagStatusTransitionException.cs`
- Create: `src/SnagList.Domain/Snags/SnagNotEditableException.cs`
- Create: `src/SnagList.Domain/Snags/SnagPhotoLimitExceededException.cs`
- Create: `src/SnagList.Domain/Snags/Snag.cs`
- Test: `tests/SnagList.Domain.Tests/Snags/SnagTransitionTests.cs`
- Test: `tests/SnagList.Domain.Tests/Snags/SnagEditTests.cs`

**Interfaces:**
- Consumes: nothing (Domain has no dependencies).
- Produces: `Snag.Report(locationId, subLocation, category, severity, description, reportedByStaffId, reportedByName, reportedAt) -> Snag`; `snag.Edit(subLocation, category, severity, description)`; `snag.TransitionTo(newStatus, changedByStaffId, changedAt)`; `snag.DomainEvents` (`IReadOnlyList<object>`); `snag.ClearDomainEvents()`. Later tasks (Application layer, Task 6+) consume all of these exactly as named here.

- [ ] **Step 1: Write the enums**

```csharp
namespace SnagList.Domain.Snags;

public enum SnagCategory
{
    Electrical,
    Plumbing,
    StructuralOrFabric,
    HeatingAndCooling,
    CleaningAndHousekeeping,
    SafetyHazard,
    Other,
}
```

```csharp
namespace SnagList.Domain.Snags;

public enum SnagSeverity
{
    Low,
    Medium,
    High,
    SafetyCritical,
}
```

```csharp
namespace SnagList.Domain.Snags;

public enum SnagStatus
{
    Reported,
    Acknowledged,
    InProgress,
    Resolved,
    Closed,
    Rejected,
    Withdrawn,
}
```

- [ ] **Step 2: Write the failing transition tests**

```csharp
namespace SnagList.Domain.Tests.Snags;

using SnagList.Domain.Snags;
using Xunit;

public class SnagTransitionTests
{
    private static Snag NewSnag() => Snag.Report(
        Guid.NewGuid(), "3rd floor, room 3.12", SnagCategory.Electrical, SnagSeverity.Medium,
        "Flickering light", "U123456", "Jane Smith", DateTimeOffset.UtcNow);

    // Walks the aggregate forward one legal step at a time until it reaches `target`,
    // so each test only has to assert the one edge it's actually checking.
    private static void DriveToStatus(Snag snag, SnagStatus target)
    {
        while (snag.Status != target)
        {
            var next = snag.Status switch
            {
                SnagStatus.Reported => SnagStatus.Acknowledged,
                SnagStatus.Acknowledged => SnagStatus.InProgress,
                SnagStatus.InProgress => SnagStatus.Resolved,
                SnagStatus.Resolved => SnagStatus.Closed,
                _ => throw new InvalidOperationException($"cannot drive from {snag.Status} toward {target}"),
            };
            snag.TransitionTo(next, "U000000", DateTimeOffset.UtcNow);
        }
    }

    [Theory]
    [InlineData(SnagStatus.Reported, SnagStatus.Acknowledged)]
    [InlineData(SnagStatus.Reported, SnagStatus.Rejected)]
    [InlineData(SnagStatus.Reported, SnagStatus.Withdrawn)]
    [InlineData(SnagStatus.Acknowledged, SnagStatus.InProgress)]
    [InlineData(SnagStatus.Acknowledged, SnagStatus.Rejected)]
    [InlineData(SnagStatus.InProgress, SnagStatus.Resolved)]
    [InlineData(SnagStatus.Resolved, SnagStatus.Closed)]
    public void Allows_every_legal_transition(SnagStatus from, SnagStatus to)
    {
        var snag = NewSnag();
        DriveToStatus(snag, from);

        snag.TransitionTo(to, "U000000", DateTimeOffset.UtcNow);

        Assert.Equal(to, snag.Status);
    }

    [Theory]
    [InlineData(SnagStatus.Reported, SnagStatus.InProgress)]
    [InlineData(SnagStatus.Reported, SnagStatus.Resolved)]
    [InlineData(SnagStatus.Reported, SnagStatus.Closed)]
    [InlineData(SnagStatus.Acknowledged, SnagStatus.Withdrawn)]
    [InlineData(SnagStatus.Acknowledged, SnagStatus.Reported)]
    [InlineData(SnagStatus.Acknowledged, SnagStatus.Resolved)]
    [InlineData(SnagStatus.InProgress, SnagStatus.Rejected)]
    [InlineData(SnagStatus.InProgress, SnagStatus.Acknowledged)]
    [InlineData(SnagStatus.InProgress, SnagStatus.Withdrawn)]
    [InlineData(SnagStatus.Resolved, SnagStatus.InProgress)]
    [InlineData(SnagStatus.Resolved, SnagStatus.Rejected)]
    [InlineData(SnagStatus.Closed, SnagStatus.Reported)]
    [InlineData(SnagStatus.Closed, SnagStatus.InProgress)]
    [InlineData(SnagStatus.Rejected, SnagStatus.Reported)]
    [InlineData(SnagStatus.Rejected, SnagStatus.Acknowledged)]
    [InlineData(SnagStatus.Withdrawn, SnagStatus.Reported)]
    public void Rejects_every_illegal_transition(SnagStatus from, SnagStatus to)
    {
        var snag = NewSnag();
        DriveToStatus(snag, from);

        var ex = Assert.Throws<InvalidSnagStatusTransitionException>(
            () => snag.TransitionTo(to, "U000000", DateTimeOffset.UtcNow));

        Assert.Equal(from, ex.From);
        Assert.Equal(to, ex.To);
    }

    [Fact]
    public void TransitionTo_raises_SnagStatusChanged_with_previous_and_new_status()
    {
        var snag = NewSnag();

        snag.TransitionTo(SnagStatus.Acknowledged, "U999999", DateTimeOffset.UtcNow);

        var evt = Assert.Single(snag.DomainEvents);
        var changed = Assert.IsType<SnagList.Domain.Snags.Events.SnagStatusChanged>(evt);
        Assert.Equal(SnagStatus.Reported, changed.PreviousStatus);
        Assert.Equal(SnagStatus.Acknowledged, changed.NewStatus);
        Assert.Equal("U999999", changed.ChangedByStaffId);
    }

    [Fact]
    public void Report_raises_SnagReported()
    {
        var snag = NewSnag();

        var evt = Assert.Single(snag.DomainEvents);
        var reported = Assert.IsType<SnagList.Domain.Snags.Events.SnagReported>(evt);
        Assert.Equal(snag.Id, reported.SnagId);
        Assert.Equal(SnagSeverity.Medium, reported.Severity);
    }
}
```

- [ ] **Step 3: Write the failing edit/photo-limit tests**

```csharp
namespace SnagList.Domain.Tests.Snags;

using SnagList.Domain.Snags;
using Xunit;

public class SnagEditTests
{
    private static Snag NewSnag() => Snag.Report(
        Guid.NewGuid(), "3rd floor", SnagCategory.Plumbing, SnagSeverity.Low,
        "Dripping tap", "U123456", "Jane Smith", DateTimeOffset.UtcNow);

    [Fact]
    public void Edit_succeeds_while_Reported()
    {
        var snag = NewSnag();

        snag.Edit("4th floor", SnagCategory.Electrical, SnagSeverity.High, "Actually a wiring issue");

        Assert.Equal("4th floor", snag.SubLocation);
        Assert.Equal(SnagCategory.Electrical, snag.Category);
        Assert.Equal(SnagSeverity.High, snag.Severity);
    }

    [Fact]
    public void Edit_throws_once_Acknowledged()
    {
        var snag = NewSnag();
        snag.TransitionTo(SnagStatus.Acknowledged, "U000000", DateTimeOffset.UtcNow);

        Assert.Throws<SnagNotEditableException>(
            () => snag.Edit("4th floor", SnagCategory.Electrical, SnagSeverity.High, "too late"));
    }

    [Fact]
    public void AddPhoto_throws_on_the_sixth_photo()
    {
        var snag = NewSnag();
        for (var i = 0; i < 5; i++)
        {
            snag.AddPhoto(new SnagPhoto($"blob-{i}", $"photo{i}.jpg", "image/jpeg", 1024, DateTimeOffset.UtcNow));
        }

        Assert.Throws<SnagPhotoLimitExceededException>(
            () => snag.AddPhoto(new SnagPhoto("blob-6", "photo6.jpg", "image/jpeg", 1024, DateTimeOffset.UtcNow)));
    }

    [Fact]
    public void Edit_and_AddPhoto_both_increment_Version()
    {
        var snag = NewSnag();
        var versionAfterReport = snag.Version;

        snag.Edit("4th floor", SnagCategory.Electrical, SnagSeverity.High, "updated");
        Assert.Equal(versionAfterReport + 1, snag.Version);

        snag.AddPhoto(new SnagPhoto("blob-0", "photo0.jpg", "image/jpeg", 1024, DateTimeOffset.UtcNow));
        Assert.Equal(versionAfterReport + 2, snag.Version);
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test tests/SnagList.Domain.Tests --filter "SnagTransitionTests|SnagEditTests"`
Expected: FAIL — `Snag`, `SnagPhoto`, the exception types, and the event types don't exist yet.

- [ ] **Step 5: Implement the exceptions**

```csharp
namespace SnagList.Domain.Snags;

public sealed class InvalidSnagStatusTransitionException : Exception
{
    public SnagStatus From { get; }
    public SnagStatus To { get; }

    public InvalidSnagStatusTransitionException(SnagStatus from, SnagStatus to)
        : base($"Cannot transition a Snag from {from} to {to}.")
    {
        From = from;
        To = to;
    }
}
```

```csharp
namespace SnagList.Domain.Snags;

public sealed class SnagNotEditableException : Exception
{
    public SnagNotEditableException(SnagStatus status)
        : base($"A Snag can only be edited or withdrawn while Reported; current status is {status}.") { }
}
```

```csharp
namespace SnagList.Domain.Snags;

public sealed class SnagPhotoLimitExceededException : Exception
{
    public SnagPhotoLimitExceededException() : base("A Snag may carry at most 5 photos.") { }
}
```

- [ ] **Step 6: Implement `SnagPhoto` (value object) and the domain events**

```csharp
namespace SnagList.Domain.Snags;

public sealed record SnagPhoto(
    string BlobKey, string FileName, string ContentType, long SizeBytes, DateTimeOffset UploadedAt);
```

```csharp
namespace SnagList.Domain.Snags.Events;

using SnagList.Domain.Snags;

public sealed record SnagReported(
    Guid SnagId, Guid LocationId, SnagSeverity Severity, string ReportedByStaffId, DateTimeOffset ReportedAt);

public sealed record SnagStatusChanged(
    Guid SnagId, SnagStatus PreviousStatus, SnagStatus NewStatus, string ChangedByStaffId,
    string ReportedByStaffId, DateTimeOffset ChangedAt);
```

`ReportedByStaffId` (distinct from `ChangedByStaffId`, the actor performing *this* transition) is
carried on the event so Task 23's notification dispatcher can email the reporter without a second
lookup — the event is self-contained rather than requiring the dispatcher to re-query the `Snag`.

- [ ] **Step 7: Implement `Snag`**

```csharp
namespace SnagList.Domain.Snags;

using SnagList.Domain.Snags.Events;

public sealed class Snag
{
    private const int MaxPhotos = 5;

    private static readonly Dictionary<SnagStatus, SnagStatus[]> AllowedTransitions = new()
    {
        [SnagStatus.Reported] = [SnagStatus.Acknowledged, SnagStatus.Rejected, SnagStatus.Withdrawn],
        [SnagStatus.Acknowledged] = [SnagStatus.InProgress, SnagStatus.Rejected],
        [SnagStatus.InProgress] = [SnagStatus.Resolved],
        [SnagStatus.Resolved] = [SnagStatus.Closed],
        [SnagStatus.Closed] = [],
        [SnagStatus.Rejected] = [],
        [SnagStatus.Withdrawn] = [],
    };

    private readonly List<SnagComment> _comments = [];
    private readonly List<SnagPhoto> _photos = [];
    private readonly List<object> _domainEvents = [];

    public Guid Id { get; private set; }
    public Guid LocationId { get; private set; }
    public string SubLocation { get; private set; } = "";
    public SnagCategory Category { get; private set; }
    public SnagSeverity Severity { get; private set; }
    public string Description { get; private set; } = "";
    public SnagStatus Status { get; private set; }
    public string ReportedByStaffId { get; private set; } = "";
    public string ReportedByName { get; private set; } = "";
    public DateTimeOffset ReportedAt { get; private set; }
    public int Version { get; private set; }

    public IReadOnlyList<SnagComment> Comments => _comments;
    public IReadOnlyList<SnagPhoto> Photos => _photos;
    public IReadOnlyList<object> DomainEvents => _domainEvents;

    private Snag() { } // EF Core

    public static Snag Report(
        Guid locationId, string subLocation, SnagCategory category, SnagSeverity severity,
        string description, string reportedByStaffId, string reportedByName, DateTimeOffset reportedAt)
    {
        var snag = new Snag
        {
            Id = Guid.NewGuid(),
            LocationId = locationId,
            SubLocation = subLocation,
            Category = category,
            Severity = severity,
            Description = description,
            Status = SnagStatus.Reported,
            ReportedByStaffId = reportedByStaffId,
            ReportedByName = reportedByName,
            ReportedAt = reportedAt,
            Version = 1,
        };
        snag._domainEvents.Add(new SnagReported(snag.Id, locationId, severity, reportedByStaffId, reportedAt));
        return snag;
    }

    public void Edit(string subLocation, SnagCategory category, SnagSeverity severity, string description)
    {
        EnsureEditable();
        SubLocation = subLocation;
        Category = category;
        Severity = severity;
        Description = description;
        Version++;
    }

    public void AddPhoto(SnagPhoto photo)
    {
        EnsureEditable();
        if (_photos.Count >= MaxPhotos) throw new SnagPhotoLimitExceededException();
        _photos.Add(photo);
        Version++;
    }

    public void AddComment(string authorStaffId, string authorName, string body, DateTimeOffset createdAt)
    {
        _comments.Add(new SnagComment(Id, authorStaffId, authorName, body, createdAt));
        Version++;
    }

    public void TransitionTo(SnagStatus newStatus, string changedByStaffId, DateTimeOffset changedAt)
    {
        if (!AllowedTransitions.TryGetValue(Status, out var allowed) || !allowed.Contains(newStatus))
        {
            throw new InvalidSnagStatusTransitionException(Status, newStatus);
        }

        var previous = Status;
        Status = newStatus;
        Version++;
        _domainEvents.Add(new SnagStatusChanged(Id, previous, newStatus, changedByStaffId, ReportedByStaffId, changedAt));
    }

    public void ClearDomainEvents() => _domainEvents.Clear();

    private void EnsureEditable()
    {
        if (Status != SnagStatus.Reported) throw new SnagNotEditableException(Status);
    }
}
```

Note: `AddComment` deliberately skips `EnsureEditable()` — per 01-domain-model.md, comments are
allowed "from either role, at any status," unlike `Edit`/`AddPhoto`/withdrawal.

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test tests/SnagList.Domain.Tests --filter "SnagTransitionTests|SnagEditTests"`
Expected: PASS (7 + 4 tests). `Snag.Comments` won't compile yet without `SnagComment` — see Task 4,
which this task's `Snag.cs` already references; write Task 4's `SnagComment.cs` before running this
step if the compiler complains about the missing type.

- [ ] **Step 9: Commit**

```bash
git add src/SnagList.Domain/Snags tests/SnagList.Domain.Tests/Snags
git commit -m "feat(domain): add Snag aggregate and status transition graph"
```

---

## Task 4: Domain — `SnagComment`, `StaffRole`, `StaffIdentity`

Do this task's Step 3 (`SnagComment`) before Task 3's Step 8 test run, since `Snag.cs` already
references `SnagComment`.

**Files:**
- Create: `src/SnagList.Domain/Snags/SnagComment.cs`
- Create: `src/SnagList.Domain/Staff/StaffRole.cs`
- Create: `src/SnagList.Domain/Staff/StaffIdentity.cs`
- Test: `tests/SnagList.Domain.Tests/Snags/SnagCommentTests.cs`
- Test: `tests/SnagList.Domain.Tests/Staff/StaffIdentityTests.cs`

**Interfaces:**
- Produces: `snag.AddComment(authorStaffId, authorName, body, createdAt)` (already called from
  `Snag`, Task 3); `StaffIdentity.FirstSeen(staffId, name, email, roles, now) -> StaffIdentity`;
  `identity.Sync(name, email, roles, now)`; properties `StaffId`, `Name`, `Email`, `Roles`
  (`IReadOnlyList<StaffRole>`), `FirstSeenAt`, `LastSeenAt`.

- [ ] **Step 1: Write the failing `SnagComment` test (via `Snag.AddComment`)**

```csharp
namespace SnagList.Domain.Tests.Snags;

using SnagList.Domain.Snags;
using Xunit;

public class SnagCommentTests
{
    [Fact]
    public void AddComment_appends_a_comment_regardless_of_status()
    {
        var snag = Snag.Report(
            Guid.NewGuid(), "3rd floor", SnagCategory.Other, SnagSeverity.Low,
            "Squeaky door", "U123456", "Jane Smith", DateTimeOffset.UtcNow);
        snag.TransitionTo(SnagStatus.Acknowledged, "U999999", DateTimeOffset.UtcNow);

        snag.AddComment("U999999", "Bob Maintenance", "Parts ordered, ETA Friday", DateTimeOffset.UtcNow);

        var comment = Assert.Single(snag.Comments);
        Assert.Equal("U999999", comment.AuthorStaffId);
        Assert.Equal("Parts ordered, ETA Friday", comment.Body);
        Assert.Equal(snag.Id, comment.SnagId);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/SnagList.Domain.Tests --filter SnagCommentTests`
Expected: FAIL — `SnagComment` does not exist.

- [ ] **Step 3: Implement `SnagComment`**

```csharp
namespace SnagList.Domain.Snags;

public sealed class SnagComment
{
    public Guid Id { get; private set; }
    public Guid SnagId { get; private set; }
    public string AuthorStaffId { get; private set; } = "";
    public string AuthorName { get; private set; } = "";
    public string Body { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }

    private SnagComment() { } // EF Core

    public SnagComment(Guid snagId, string authorStaffId, string authorName, string body, DateTimeOffset createdAt)
    {
        Id = Guid.NewGuid();
        SnagId = snagId;
        AuthorStaffId = authorStaffId;
        AuthorName = authorName;
        Body = body;
        CreatedAt = createdAt;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/SnagList.Domain.Tests --filter SnagCommentTests`
Expected: PASS.

- [ ] **Step 5: Write the failing `StaffIdentity` tests**

```csharp
namespace SnagList.Domain.Tests.Staff;

using SnagList.Domain.Staff;
using Xunit;

public class StaffIdentityTests
{
    [Fact]
    public void FirstSeen_sets_FirstSeenAt_and_LastSeenAt_to_the_same_instant()
    {
        var now = DateTimeOffset.UtcNow;

        var identity = StaffIdentity.FirstSeen("U123456", "Jane Smith", "jane@example.com", [StaffRole.Staff], now);

        Assert.Equal("U123456", identity.StaffId);
        Assert.Equal(now, identity.FirstSeenAt);
        Assert.Equal(now, identity.LastSeenAt);
        Assert.Equal([StaffRole.Staff], identity.Roles);
    }

    [Fact]
    public void Sync_updates_roles_and_LastSeenAt_but_not_FirstSeenAt()
    {
        var firstSeen = DateTimeOffset.UtcNow;
        var identity = StaffIdentity.FirstSeen("U123456", "Jane Smith", "jane@example.com", [StaffRole.Staff], firstSeen);
        var syncedAt = firstSeen.AddDays(1);

        identity.Sync("Jane Smith", "jane@example.com", [StaffRole.Staff, StaffRole.Maintenance], syncedAt);

        Assert.Equal([StaffRole.Staff, StaffRole.Maintenance], identity.Roles);
        Assert.Equal(syncedAt, identity.LastSeenAt);
        Assert.Equal(firstSeen, identity.FirstSeenAt);
    }
}
```

- [ ] **Step 6: Run tests to verify they fail**

Run: `dotnet test tests/SnagList.Domain.Tests --filter StaffIdentityTests`
Expected: FAIL — `StaffRole` and `StaffIdentity` do not exist.

- [ ] **Step 7: Implement `StaffRole` and `StaffIdentity`**

```csharp
namespace SnagList.Domain.Staff;

public enum StaffRole
{
    Staff,
    Maintenance,
}
```

```csharp
namespace SnagList.Domain.Staff;

public sealed class StaffIdentity
{
    public string StaffId { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string Email { get; private set; } = "";
    public IReadOnlyList<StaffRole> Roles { get; private set; } = [];
    public DateTimeOffset FirstSeenAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }

    private StaffIdentity() { } // EF Core

    public static StaffIdentity FirstSeen(
        string staffId, string name, string email, IReadOnlyList<StaffRole> roles, DateTimeOffset now) => new()
    {
        StaffId = staffId,
        Name = name,
        Email = email,
        Roles = roles,
        FirstSeenAt = now,
        LastSeenAt = now,
    };

    public void Sync(string name, string email, IReadOnlyList<StaffRole> roles, DateTimeOffset now)
    {
        Name = name;
        Email = email;
        Roles = roles;
        LastSeenAt = now;
    }
}
```

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test tests/SnagList.Domain.Tests`
Expected: PASS — the full `SnagList.Domain.Tests` suite (Tasks 2–4) is green.

- [ ] **Step 9: Commit**

```bash
git add src/SnagList.Domain/Staff src/SnagList.Domain/Snags/SnagComment.cs \
  tests/SnagList.Domain.Tests/Snags/SnagCommentTests.cs tests/SnagList.Domain.Tests/Staff
git commit -m "feat(domain): add SnagComment, StaffRole, StaffIdentity"
```

---

## Task 5: Application — ports, cursor pagination utility, and `Location` commands/query

**Files:**
- Create: `src/SnagList.Application/Abstractions/ILocationRepository.cs`
- Create: `src/SnagList.Application/Abstractions/ISnagRepository.cs`
- Create: `src/SnagList.Application/Abstractions/IUnitOfWork.cs`
- Create: `src/SnagList.Application/Abstractions/IClock.cs`
- Create: `src/SnagList.Application/Abstractions/IBlobStorage.cs`
- Create: `src/SnagList.Application/Abstractions/IEmailSender.cs`
- Create: `src/SnagList.Application/Abstractions/IAuditWriter.cs`
- Create: `src/SnagList.Application/Abstractions/ILocationQueries.cs`
- Create: `src/SnagList.Application/Abstractions/ISnagQueries.cs`
- Create: `src/SnagList.Application/Common/CursorPage.cs`
- Create: `src/SnagList.Application/Common/OpaqueCursor.cs`
- Create: `src/SnagList.Application/Locations/LocationNotFoundException.cs`
- Create: `src/SnagList.Application/Locations/Commands/CreateLocationCommand.cs`
- Create: `src/SnagList.Application/Locations/Commands/UpdateLocationCommand.cs`
- Create: `src/SnagList.Application/Locations/Commands/RetireLocationCommand.cs`
- Create: `src/SnagList.Application/Locations/Queries/ListLocationsQuery.cs`
- Test: `tests/SnagList.Application.Tests/Common/OpaqueCursorTests.cs`
- Test: `tests/SnagList.Application.Tests/Locations/LocationCommandTests.cs`
- Test: `tests/SnagList.Application.Tests/Testing/FakeLocationRepository.cs` (test double, not xUnit test)
- Test: `tests/SnagList.Application.Tests/Testing/FakeUnitOfWork.cs` (test double, not xUnit test)

**Interfaces:**
- Consumes: `Location` from `SnagList.Domain.Locations` (Task 2), `Snag`/`SnagComment`/`SnagPhoto`
  from `SnagList.Domain.Snags` (Tasks 3–4).
- Produces (read by every later Application task and by Infrastructure, Task 11+):
  `ILocationRepository.GetAsync(id, ct)`, `.Add(location)`; `ISnagRepository.GetAsync(id, ct)`,
  `.Add(snag)`; `IUnitOfWork.SaveChangesAsync(ct)`; `IClock.UtcNow`; `IBlobStorage.PutAsync`,
  `.GetAsync`, `.GetPresignedGetUrlAsync`, `.DeleteAsync`; `IEmailSender.SendAsync(to, subject,
  body, ct)`; `IAuditWriter.WriteAsync(actorStaffId, action, entityType, entityId, occurredAt, ct)`;
  `CursorPage<T>(Items, NextCursor)`; `OpaqueCursor.Encode<T>(key) -> string`,
  `OpaqueCursor.Decode<T>(cursor) -> T?`.

- [ ] **Step 1: Write the failing `OpaqueCursor` tests**

```csharp
namespace SnagList.Application.Tests.Common;

using SnagList.Application.Common;
using Xunit;

public class OpaqueCursorTests
{
    private sealed record Key(string Name, Guid Id);

    [Fact]
    public void Decode_of_Encode_round_trips()
    {
        var key = new Key("Head Office", Guid.NewGuid());

        var encoded = OpaqueCursor.Encode(key);
        var decoded = OpaqueCursor.Decode<Key>(encoded);

        Assert.Equal(key, decoded);
    }

    [Fact]
    public void Encoded_cursor_contains_no_padding_or_url_unsafe_characters()
    {
        var encoded = OpaqueCursor.Encode(new Key("Head Office", Guid.NewGuid()));

        Assert.DoesNotContain('+', encoded);
        Assert.DoesNotContain('/', encoded);
        Assert.DoesNotContain('=', encoded);
    }

    [Fact]
    public void Decode_of_null_or_empty_returns_default()
    {
        Assert.Null(OpaqueCursor.Decode<Key>(null));
        Assert.Null(OpaqueCursor.Decode<Key>(""));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/SnagList.Application.Tests --filter OpaqueCursorTests`
Expected: FAIL — `SnagList.Application.Common` does not exist yet.

- [ ] **Step 3: Implement `CursorPage` and `OpaqueCursor`**

```csharp
namespace SnagList.Application.Common;

public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor);
```

```csharp
namespace SnagList.Application.Common;

using System.Text.Json;

public static class OpaqueCursor
{
    public static string Encode<T>(T key) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(key))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    public static T? Decode<T>(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor)) return default;

        var base64 = cursor.Replace('-', '+').Replace('_', '/');
        base64 += new string('=', (4 - (base64.Length % 4)) % 4);
        return JsonSerializer.Deserialize<T>(Convert.FromBase64String(base64));
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/SnagList.Application.Tests --filter OpaqueCursorTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Write the ports**

```csharp
namespace SnagList.Application.Abstractions;

using SnagList.Domain.Locations;

public interface ILocationRepository
{
    Task<Location?> GetAsync(Guid id, CancellationToken ct);
    void Add(Location location);
}
```

```csharp
namespace SnagList.Application.Abstractions;

using SnagList.Domain.Snags;

public interface ISnagRepository
{
    Task<Snag?> GetAsync(Guid id, CancellationToken ct);
    void Add(Snag snag);
}
```

```csharp
namespace SnagList.Application.Abstractions;

public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct);
}
```

```csharp
namespace SnagList.Application.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
```

```csharp
namespace SnagList.Application.Abstractions;

public interface IBlobStorage
{
    Task PutAsync(string key, Stream content, string contentType, CancellationToken ct);
    Task<Stream> GetAsync(string key, CancellationToken ct);
    Task<Uri> GetPresignedGetUrlAsync(string key, TimeSpan expiry, CancellationToken ct);
    Task DeleteAsync(string key, CancellationToken ct);
}
```

```csharp
namespace SnagList.Application.Abstractions;

public interface IEmailSender
{
    Task SendAsync(string toAddress, string subject, string body, CancellationToken ct);
}
```

```csharp
namespace SnagList.Application.Abstractions;

public interface IAuditWriter
{
    Task WriteAsync(
        string actorStaffId, string action, string entityType, Guid entityId,
        DateTimeOffset occurredAt, CancellationToken ct);
}
```

```csharp
namespace SnagList.Application.Abstractions;

using SnagList.Application.Common;
using SnagList.Application.Locations.Queries;

public interface ILocationQueries
{
    Task<CursorPage<LocationSummary>> ListAsync(ListLocationsQuery query, CancellationToken ct);
}
```

`ISnagQueries` is written in Task 10, once `SnagSummary`/`SnagDetail` exist — `ISnagRepository`
above is the write-side port this task needs now; the read-side port waits until its DTOs do.

- [ ] **Step 6: Write the failing `Location` command tests, against fakes**

```csharp
namespace SnagList.Application.Tests.Testing;

using SnagList.Application.Abstractions;
using SnagList.Domain.Locations;

public sealed class FakeLocationRepository : ILocationRepository
{
    public readonly Dictionary<Guid, Location> Store = [];

    public Task<Location?> GetAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(Store.GetValueOrDefault(id));

    public void Add(Location location) => Store[location.Id] = location;
}
```

```csharp
namespace SnagList.Application.Tests.Testing;

using SnagList.Application.Abstractions;

public sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveChangesCallCount { get; private set; }

    public Task SaveChangesAsync(CancellationToken ct)
    {
        SaveChangesCallCount++;
        return Task.CompletedTask;
    }
}
```

```csharp
namespace SnagList.Application.Tests.Locations;

using SnagList.Application.Locations.Commands;
using SnagList.Application.Tests.Testing;
using Xunit;

public class LocationCommandTests
{
    [Fact]
    public async Task CreateLocationCommandHandler_adds_an_active_location_and_saves()
    {
        var repository = new FakeLocationRepository();
        var unitOfWork = new FakeUnitOfWork();
        var handler = new CreateLocationCommandHandler(repository, unitOfWork);

        var id = await handler.HandleAsync(new CreateLocationCommand("Head Office", "1 Main St"), default);

        var stored = repository.Store[id];
        Assert.Equal("Head Office", stored.Name);
        Assert.True(stored.IsActive);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task UpdateLocationCommandHandler_updates_an_existing_location()
    {
        var repository = new FakeLocationRepository();
        var unitOfWork = new FakeUnitOfWork();
        var location = SnagList.Domain.Locations.Location.Create("Head Office", "1 Main St");
        repository.Add(location);
        var handler = new UpdateLocationCommandHandler(repository, unitOfWork);

        await handler.HandleAsync(new UpdateLocationCommand(location.Id, "Renamed", "2 Main St"), default);

        Assert.Equal("Renamed", repository.Store[location.Id].Name);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task UpdateLocationCommandHandler_throws_when_not_found()
    {
        var handler = new UpdateLocationCommandHandler(new FakeLocationRepository(), new FakeUnitOfWork());

        await Assert.ThrowsAsync<LocationNotFoundException>(
            () => handler.HandleAsync(new UpdateLocationCommand(Guid.NewGuid(), "X", "Y"), default));
    }

    [Fact]
    public async Task RetireLocationCommandHandler_sets_IsActive_false()
    {
        var repository = new FakeLocationRepository();
        var unitOfWork = new FakeUnitOfWork();
        var location = SnagList.Domain.Locations.Location.Create("Head Office", "1 Main St");
        repository.Add(location);
        var handler = new RetireLocationCommandHandler(repository, unitOfWork);

        await handler.HandleAsync(new RetireLocationCommand(location.Id), default);

        Assert.False(repository.Store[location.Id].IsActive);
    }
}
```

- [ ] **Step 7: Run tests to verify they fail**

Run: `dotnet test tests/SnagList.Application.Tests --filter LocationCommandTests`
Expected: FAIL — the command/handler types don't exist yet.

- [ ] **Step 8: Implement the `Location` commands**

```csharp
namespace SnagList.Application.Locations;

public sealed class LocationNotFoundException(Guid id)
    : Exception($"Location {id} was not found.");
```

```csharp
namespace SnagList.Application.Locations.Commands;

using SnagList.Application.Abstractions;
using SnagList.Domain.Locations;

public sealed record CreateLocationCommand(string Name, string Address);

public sealed class CreateLocationCommandHandler(ILocationRepository repository, IUnitOfWork unitOfWork)
{
    public async Task<Guid> HandleAsync(CreateLocationCommand command, CancellationToken ct)
    {
        var location = Location.Create(command.Name, command.Address);
        repository.Add(location);
        await unitOfWork.SaveChangesAsync(ct);
        return location.Id;
    }
}
```

```csharp
namespace SnagList.Application.Locations.Commands;

using SnagList.Application.Abstractions;
using SnagList.Application.Locations;

public sealed record UpdateLocationCommand(Guid Id, string Name, string Address);

public sealed class UpdateLocationCommandHandler(ILocationRepository repository, IUnitOfWork unitOfWork)
{
    public async Task HandleAsync(UpdateLocationCommand command, CancellationToken ct)
    {
        var location = await repository.GetAsync(command.Id, ct)
            ?? throw new LocationNotFoundException(command.Id);
        location.Update(command.Name, command.Address);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
```

```csharp
namespace SnagList.Application.Locations.Commands;

using SnagList.Application.Abstractions;
using SnagList.Application.Locations;

public sealed record RetireLocationCommand(Guid Id);

public sealed class RetireLocationCommandHandler(ILocationRepository repository, IUnitOfWork unitOfWork)
{
    public async Task HandleAsync(RetireLocationCommand command, CancellationToken ct)
    {
        var location = await repository.GetAsync(command.Id, ct)
            ?? throw new LocationNotFoundException(command.Id);
        location.Retire();
        await unitOfWork.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 9: Run tests to verify they pass**

Run: `dotnet test tests/SnagList.Application.Tests --filter LocationCommandTests`
Expected: PASS (4 tests).

- [ ] **Step 10: Add the `ListLocations` query contract (implementation lands in Task 12)**

```csharp
namespace SnagList.Application.Locations.Queries;

public sealed record ListLocationsQuery(bool IncludeRetired, string? Cursor, int Limit);

public sealed record LocationSummary(Guid Id, string Name, string Address, bool IsActive);

public sealed record LocationListCursorKey(string Name, Guid Id);

public sealed class ListLocationsQueryHandler(SnagList.Application.Abstractions.ILocationQueries queries)
{
    public Task<SnagList.Application.Common.CursorPage<LocationSummary>> HandleAsync(
        ListLocationsQuery query, CancellationToken ct) => queries.ListAsync(query, ct);
}
```

No test here yet — `ListLocationsQueryHandler` is a pure passthrough with nothing of its own to
assert; it's exercised end-to-end once `ISnagQueries`'s sibling, `ILocationQueries`, gets its real
EF-backed implementation in Task 12 and an API integration test drives it in Task 17.

- [ ] **Step 11: Commit**

```bash
git add src/SnagList.Application tests/SnagList.Application.Tests
git commit -m "feat(application): add ports, cursor pagination, and Location commands/query"
```

---

## Task 6: Application — `ReportSnag` command

**Files:**
- Create: `src/SnagList.Application/Snags/SnagNotFoundException.cs`
- Create: `src/SnagList.Application/Snags/LocationNotActiveException.cs`
- Create: `src/SnagList.Application/Snags/Commands/ReportSnagCommand.cs`
- Test: `tests/SnagList.Application.Tests/Testing/FakeSnagRepository.cs` (test double)
- Test: `tests/SnagList.Application.Tests/Snags/ReportSnagCommandTests.cs`

**Interfaces:**
- Consumes: `ILocationRepository`, `ISnagRepository`, `IUnitOfWork` (Task 5); `Snag.Report(...)`
  (Task 3).
- Produces: `ReportSnagCommandHandler.HandleAsync(ReportSnagCommand, ct) -> Guid` (the new `Snag`'s
  id) — consumed by the API endpoint in Task 18.

- [ ] **Step 1: Write the failing tests**

```csharp
namespace SnagList.Application.Tests.Testing;

using SnagList.Application.Abstractions;
using SnagList.Domain.Snags;

public sealed class FakeSnagRepository : ISnagRepository
{
    public readonly Dictionary<Guid, Snag> Store = [];

    public Task<Snag?> GetAsync(Guid id, CancellationToken ct) => Task.FromResult(Store.GetValueOrDefault(id));

    public void Add(Snag snag) => Store[snag.Id] = snag;
}
```

```csharp
namespace SnagList.Application.Tests.Snags;

using SnagList.Application.Snags;
using SnagList.Application.Snags.Commands;
using SnagList.Application.Tests.Testing;
using SnagList.Domain.Locations;
using SnagList.Domain.Snags;
using Xunit;

public class ReportSnagCommandTests
{
    private static (FakeLocationRepository locations, FakeSnagRepository snags, FakeUnitOfWork uow, ReportSnagCommandHandler handler)
        Build()
    {
        var locations = new FakeLocationRepository();
        var snags = new FakeSnagRepository();
        var uow = new FakeUnitOfWork();
        var handler = new ReportSnagCommandHandler(locations, snags, uow);
        return (locations, snags, uow, handler);
    }

    [Fact]
    public async Task Reports_a_Snag_against_an_active_Location()
    {
        var (locations, snags, uow, handler) = Build();
        var location = Location.Create("Head Office", "1 Main St");
        locations.Add(location);

        var id = await handler.HandleAsync(
            new ReportSnagCommand(
                location.Id, "3rd floor", SnagCategory.Electrical, SnagSeverity.Medium,
                "Flickering light", "U123456", "Jane Smith"),
            default);

        var stored = snags.Store[id];
        Assert.Equal(SnagStatus.Reported, stored.Status);
        Assert.Equal(location.Id, stored.LocationId);
        Assert.Equal(1, uow.SaveChangesCallCount);
    }

    [Fact]
    public async Task Throws_when_the_Location_does_not_exist()
    {
        var (_, _, _, handler) = Build();

        await Assert.ThrowsAsync<SnagList.Application.Locations.LocationNotFoundException>(() => handler.HandleAsync(
            new ReportSnagCommand(
                Guid.NewGuid(), "3rd floor", SnagCategory.Electrical, SnagSeverity.Medium,
                "Flickering light", "U123456", "Jane Smith"),
            default));
    }

    [Fact]
    public async Task Throws_when_the_Location_is_retired()
    {
        var (locations, _, _, handler) = Build();
        var location = Location.Create("Old Site", "1 Main St");
        location.Retire();
        locations.Add(location);

        await Assert.ThrowsAsync<LocationNotActiveException>(() => handler.HandleAsync(
            new ReportSnagCommand(
                location.Id, "3rd floor", SnagCategory.Electrical, SnagSeverity.Medium,
                "Flickering light", "U123456", "Jane Smith"),
            default));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/SnagList.Application.Tests --filter ReportSnagCommandTests`
Expected: FAIL — `ReportSnagCommand`/`ReportSnagCommandHandler` don't exist.

- [ ] **Step 3: Implement**

```csharp
namespace SnagList.Application.Snags;

public sealed class SnagNotFoundException(Guid id) : Exception($"Snag {id} was not found.");
```

```csharp
namespace SnagList.Application.Snags;

public sealed class LocationNotActiveException(Guid locationId)
    : Exception($"Location {locationId} is retired and cannot accept new Snag reports.");
```

```csharp
namespace SnagList.Application.Snags.Commands;

using SnagList.Application.Abstractions;
using SnagList.Application.Locations;
using SnagList.Application.Snags;
using SnagList.Domain.Snags;

public sealed record ReportSnagCommand(
    Guid LocationId, string SubLocation, SnagCategory Category, SnagSeverity Severity,
    string Description, string ReportedByStaffId, string ReportedByName);

public sealed class ReportSnagCommandHandler(
    ILocationRepository locationRepository, ISnagRepository snagRepository, IUnitOfWork unitOfWork)
{
    public async Task<Guid> HandleAsync(ReportSnagCommand command, CancellationToken ct)
    {
        var location = await locationRepository.GetAsync(command.LocationId, ct)
            ?? throw new LocationNotFoundException(command.LocationId);
        if (!location.IsActive) throw new LocationNotActiveException(command.LocationId);

        var snag = Snag.Report(
            command.LocationId, command.SubLocation, command.Category, command.Severity,
            command.Description, command.ReportedByStaffId, command.ReportedByName, DateTimeOffset.UtcNow);

        snagRepository.Add(snag);
        await unitOfWork.SaveChangesAsync(ct);
        return snag.Id;
    }
}
```

`DateTimeOffset.UtcNow` is used directly here rather than `IClock` because nothing in this
handler's tests asserts a specific reported-at instant; `IClock` is introduced in Task 7 where
`WithdrawSnag`'s test *does* need a controllable clock.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/SnagList.Application.Tests --filter ReportSnagCommandTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/SnagList.Application/Snags tests/SnagList.Application.Tests/Snags/ReportSnagCommandTests.cs \
  tests/SnagList.Application.Tests/Testing/FakeSnagRepository.cs
git commit -m "feat(application): add ReportSnag command"
```

---

## Task 7: Application — `EditSnag` and `WithdrawSnag` commands (concurrency + reporter-only authorization)

Introduces the two cross-cutting checks every later mutating `Snag` command reuses: the
`expectedVersion` conflict check (Global Constraints) and the reporter-only authorization check.

**Files:**
- Create: `src/SnagList.Application/Snags/SnagVersionConflictException.cs`
- Create: `src/SnagList.Application/Snags/UnauthorizedSnagActionException.cs`
- Create: `src/SnagList.Application/Abstractions/IClock.cs` (already declared in Task 5 — this task
  is the first to actually consume it)
- Create: `src/SnagList.Application/Snags/Commands/EditSnagCommand.cs`
- Create: `src/SnagList.Application/Snags/Commands/WithdrawSnagCommand.cs`
- Test: `tests/SnagList.Application.Tests/Testing/FakeClock.cs` (test double)
- Test: `tests/SnagList.Application.Tests/Snags/EditSnagCommandTests.cs`
- Test: `tests/SnagList.Application.Tests/Snags/WithdrawSnagCommandTests.cs`

**Interfaces:**
- Consumes: `ISnagRepository`, `IUnitOfWork`, `IClock` (Task 5); `snag.Edit(...)`,
  `snag.TransitionTo(...)` (Task 3).
- Produces: `EditSnagCommandHandler.HandleAsync(EditSnagCommand, ct)`,
  `WithdrawSnagCommandHandler.HandleAsync(WithdrawSnagCommand, ct)` — both consumed by the API
  endpoints in Task 18. `SnagVersionConflictException`/`UnauthorizedSnagActionException` are
  consumed by every later `Snag`-mutating command (Tasks 8–9) and mapped to HTTP responses in
  Task 16 (409 and 403 respectively).

- [ ] **Step 1: Write the failing tests**

```csharp
namespace SnagList.Application.Tests.Testing;

using SnagList.Application.Abstractions;

public sealed class FakeClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
}
```

```csharp
namespace SnagList.Application.Tests.Snags;

using SnagList.Application.Snags;
using SnagList.Application.Snags.Commands;
using SnagList.Application.Tests.Testing;
using SnagList.Domain.Snags;
using Xunit;

public class EditSnagCommandTests
{
    private static Snag ReportedSnag(out FakeSnagRepository repo)
    {
        var snag = Snag.Report(
            Guid.NewGuid(), "3rd floor", SnagCategory.Plumbing, SnagSeverity.Low,
            "Dripping tap", "U123456", "Jane Smith", DateTimeOffset.UtcNow);
        repo = new FakeSnagRepository();
        repo.Add(snag);
        return snag;
    }

    [Fact]
    public async Task Edits_a_Reported_Snag_when_the_reporter_calls_and_version_matches()
    {
        var snag = ReportedSnag(out var repo);
        var handler = new EditSnagCommandHandler(repo, new FakeUnitOfWork());

        await handler.HandleAsync(
            new EditSnagCommand(snag.Id, "U123456", "4th floor", SnagCategory.Electrical, SnagSeverity.High,
                "Actually wiring", snag.Version),
            default);

        Assert.Equal("4th floor", repo.Store[snag.Id].SubLocation);
    }

    [Fact]
    public async Task Throws_when_a_different_staff_member_tries_to_edit()
    {
        var snag = ReportedSnag(out var repo);
        var handler = new EditSnagCommandHandler(repo, new FakeUnitOfWork());

        await Assert.ThrowsAsync<UnauthorizedSnagActionException>(() => handler.HandleAsync(
            new EditSnagCommand(snag.Id, "U999999", "4th floor", SnagCategory.Electrical, SnagSeverity.High,
                "Actually wiring", snag.Version),
            default));
    }

    [Fact]
    public async Task Throws_a_version_conflict_when_expectedVersion_is_stale()
    {
        var snag = ReportedSnag(out var repo);
        var handler = new EditSnagCommandHandler(repo, new FakeUnitOfWork());

        var ex = await Assert.ThrowsAsync<SnagVersionConflictException>(() => handler.HandleAsync(
            new EditSnagCommand(snag.Id, "U123456", "4th floor", SnagCategory.Electrical, SnagSeverity.High,
                "Actually wiring", snag.Version + 1),
            default));

        Assert.Equal(snag.Version, ex.ActualVersion);
    }

    [Fact]
    public async Task Throws_SnagNotEditableException_once_Acknowledged()
    {
        var snag = ReportedSnag(out var repo);
        snag.TransitionTo(SnagStatus.Acknowledged, "U999999", DateTimeOffset.UtcNow);
        var handler = new EditSnagCommandHandler(repo, new FakeUnitOfWork());

        await Assert.ThrowsAsync<SnagNotEditableException>(() => handler.HandleAsync(
            new EditSnagCommand(snag.Id, "U123456", "4th floor", SnagCategory.Electrical, SnagSeverity.High,
                "too late", snag.Version),
            default));
    }
}
```

```csharp
namespace SnagList.Application.Tests.Snags;

using SnagList.Application.Snags;
using SnagList.Application.Snags.Commands;
using SnagList.Application.Tests.Testing;
using SnagList.Domain.Snags;
using Xunit;

public class WithdrawSnagCommandTests
{
    [Fact]
    public async Task Withdraws_a_Reported_Snag_when_the_reporter_calls()
    {
        var snag = Snag.Report(
            Guid.NewGuid(), "3rd floor", SnagCategory.Other, SnagSeverity.Low,
            "Squeaky door", "U123456", "Jane Smith", DateTimeOffset.UtcNow);
        var repo = new FakeSnagRepository();
        repo.Add(snag);
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var handler = new WithdrawSnagCommandHandler(repo, new FakeUnitOfWork(), clock);

        await handler.HandleAsync(new WithdrawSnagCommand(snag.Id, "U123456", snag.Version), default);

        Assert.Equal(SnagStatus.Withdrawn, repo.Store[snag.Id].Status);
    }

    [Fact]
    public async Task Throws_when_a_different_staff_member_tries_to_withdraw()
    {
        var snag = Snag.Report(
            Guid.NewGuid(), "3rd floor", SnagCategory.Other, SnagSeverity.Low,
            "Squeaky door", "U123456", "Jane Smith", DateTimeOffset.UtcNow);
        var repo = new FakeSnagRepository();
        repo.Add(snag);
        var handler = new WithdrawSnagCommandHandler(repo, new FakeUnitOfWork(), new FakeClock(DateTimeOffset.UtcNow));

        await Assert.ThrowsAsync<UnauthorizedSnagActionException>(
            () => handler.HandleAsync(new WithdrawSnagCommand(snag.Id, "U999999", snag.Version), default));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/SnagList.Application.Tests --filter "EditSnagCommandTests|WithdrawSnagCommandTests"`
Expected: FAIL — the command/handler/exception types don't exist yet.

- [ ] **Step 3: Implement the shared exceptions**

```csharp
namespace SnagList.Application.Snags;

public sealed class SnagVersionConflictException(Guid snagId, int expectedVersion, int actualVersion)
    : Exception($"Snag {snagId} version conflict: expected {expectedVersion}, actual {actualVersion}.")
{
    public Guid SnagId { get; } = snagId;
    public int ExpectedVersion { get; } = expectedVersion;
    public int ActualVersion { get; } = actualVersion;
}
```

```csharp
namespace SnagList.Application.Snags;

public sealed class UnauthorizedSnagActionException(string action)
    : Exception($"Not authorized to {action} this Snag.");
```

- [ ] **Step 4: Implement `EditSnag` and `WithdrawSnag`**

```csharp
namespace SnagList.Application.Snags.Commands;

using SnagList.Application.Abstractions;
using SnagList.Application.Snags;
using SnagList.Domain.Snags;

public sealed record EditSnagCommand(
    Guid SnagId, string ActingStaffId, string SubLocation, SnagCategory Category,
    SnagSeverity Severity, string Description, int ExpectedVersion);

public sealed class EditSnagCommandHandler(ISnagRepository repository, IUnitOfWork unitOfWork)
{
    public async Task HandleAsync(EditSnagCommand command, CancellationToken ct)
    {
        var snag = await repository.GetAsync(command.SnagId, ct)
            ?? throw new SnagNotFoundException(command.SnagId);

        if (snag.ReportedByStaffId != command.ActingStaffId)
        {
            throw new UnauthorizedSnagActionException("edit");
        }
        if (snag.Version != command.ExpectedVersion)
        {
            throw new SnagVersionConflictException(snag.Id, command.ExpectedVersion, snag.Version);
        }

        snag.Edit(command.SubLocation, command.Category, command.Severity, command.Description);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
```

```csharp
namespace SnagList.Application.Snags.Commands;

using SnagList.Application.Abstractions;
using SnagList.Application.Snags;
using SnagList.Domain.Snags;

public sealed record WithdrawSnagCommand(Guid SnagId, string ActingStaffId, int ExpectedVersion);

public sealed class WithdrawSnagCommandHandler(ISnagRepository repository, IUnitOfWork unitOfWork, IClock clock)
{
    public async Task HandleAsync(WithdrawSnagCommand command, CancellationToken ct)
    {
        var snag = await repository.GetAsync(command.SnagId, ct)
            ?? throw new SnagNotFoundException(command.SnagId);

        if (snag.ReportedByStaffId != command.ActingStaffId)
        {
            throw new UnauthorizedSnagActionException("withdraw");
        }
        if (snag.Version != command.ExpectedVersion)
        {
            throw new SnagVersionConflictException(snag.Id, command.ExpectedVersion, snag.Version);
        }

        snag.TransitionTo(SnagStatus.Withdrawn, command.ActingStaffId, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
```

Note the authorization check runs *before* the version check in both handlers: a caller who isn't
the reporter should get "not authorized," not a version-conflict message that leaks the record's
current state to someone who shouldn't be modifying it at all.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/SnagList.Application.Tests --filter "EditSnagCommandTests|WithdrawSnagCommandTests"`
Expected: PASS (4 + 2 tests).

- [ ] **Step 6: Commit**

```bash
git add src/SnagList.Application/Snags tests/SnagList.Application.Tests/Snags/EditSnagCommandTests.cs \
  tests/SnagList.Application.Tests/Snags/WithdrawSnagCommandTests.cs tests/SnagList.Application.Tests/Testing/FakeClock.cs
git commit -m "feat(application): add EditSnag and WithdrawSnag commands"
```

---

## Task 8: Application — Maintenance status-transition commands (`Acknowledge`/`Start`/`Resolve`/`Close`, and `Reject`)

`Acknowledge`/`Start`/`Resolve`/`Close` are structurally identical — load, check version, call
`TransitionTo`, save — so they share one `ChangeSnagStatusCommand`/handler rather than four
near-duplicate classes; the API layer (Task 18) exposes four distinct endpoints, each constructing
this one command with a different `TargetStatus`. `Reject` is genuinely different: it always
carries a reason, recorded as a comment in the same save as the transition, so it gets its own
command. None of these re-check the caller's role — that's `MaintenancePolicy`, a plain role check
fully expressible as an ASP.NET Core authorization policy at the endpoint (Task 15), unlike the
reporter-only check in Task 7 which needed runtime data the policy system can't see.

**Files:**
- Create: `src/SnagList.Application/Snags/Commands/ChangeSnagStatusCommand.cs`
- Create: `src/SnagList.Application/Snags/Commands/RejectSnagCommand.cs`
- Test: `tests/SnagList.Application.Tests/Snags/ChangeSnagStatusCommandTests.cs`
- Test: `tests/SnagList.Application.Tests/Snags/RejectSnagCommandTests.cs`

**Interfaces:**
- Consumes: `ISnagRepository`, `IUnitOfWork`, `IClock` (Task 5); `snag.TransitionTo(...)`,
  `snag.AddComment(...)` (Task 3); `SnagVersionConflictException` (Task 7).
- Produces: `ChangeSnagStatusCommandHandler.HandleAsync(ChangeSnagStatusCommand, ct)`,
  `RejectSnagCommandHandler.HandleAsync(RejectSnagCommand, ct)` — both consumed by the API
  endpoints in Task 19.

- [ ] **Step 1: Write the failing tests**

```csharp
namespace SnagList.Application.Tests.Snags;

using SnagList.Application.Snags;
using SnagList.Application.Snags.Commands;
using SnagList.Application.Tests.Testing;
using SnagList.Domain.Snags;
using Xunit;

public class ChangeSnagStatusCommandTests
{
    private static Snag NewSnag() => Snag.Report(
        Guid.NewGuid(), "3rd floor", SnagCategory.Electrical, SnagSeverity.Medium,
        "Flickering light", "U123456", "Jane Smith", DateTimeOffset.UtcNow);

    [Fact]
    public async Task Transitions_to_the_target_status_when_the_edge_is_legal()
    {
        var snag = NewSnag();
        var repo = new FakeSnagRepository();
        repo.Add(snag);
        var handler = new ChangeSnagStatusCommandHandler(repo, new FakeUnitOfWork(), new FakeClock(DateTimeOffset.UtcNow));

        await handler.HandleAsync(
            new ChangeSnagStatusCommand(snag.Id, SnagStatus.Acknowledged, "U999999", snag.Version), default);

        Assert.Equal(SnagStatus.Acknowledged, repo.Store[snag.Id].Status);
    }

    [Fact]
    public async Task Throws_InvalidSnagStatusTransitionException_for_an_illegal_edge()
    {
        var snag = NewSnag();
        var repo = new FakeSnagRepository();
        repo.Add(snag);
        var handler = new ChangeSnagStatusCommandHandler(repo, new FakeUnitOfWork(), new FakeClock(DateTimeOffset.UtcNow));

        await Assert.ThrowsAsync<InvalidSnagStatusTransitionException>(() => handler.HandleAsync(
            new ChangeSnagStatusCommand(snag.Id, SnagStatus.Resolved, "U999999", snag.Version), default));
    }

    [Fact]
    public async Task Throws_a_version_conflict_when_expectedVersion_is_stale()
    {
        var snag = NewSnag();
        var repo = new FakeSnagRepository();
        repo.Add(snag);
        var handler = new ChangeSnagStatusCommandHandler(repo, new FakeUnitOfWork(), new FakeClock(DateTimeOffset.UtcNow));

        await Assert.ThrowsAsync<SnagVersionConflictException>(() => handler.HandleAsync(
            new ChangeSnagStatusCommand(snag.Id, SnagStatus.Acknowledged, "U999999", snag.Version + 1), default));
    }
}
```

```csharp
namespace SnagList.Application.Tests.Snags;

using SnagList.Application.Snags.Commands;
using SnagList.Application.Tests.Testing;
using SnagList.Domain.Snags;
using Xunit;

public class RejectSnagCommandTests
{
    [Fact]
    public async Task Rejects_a_Snag_and_records_the_reason_as_a_comment()
    {
        var snag = Snag.Report(
            Guid.NewGuid(), "3rd floor", SnagCategory.Other, SnagSeverity.Low,
            "Reported twice by mistake", "U123456", "Jane Smith", DateTimeOffset.UtcNow);
        var repo = new FakeSnagRepository();
        repo.Add(snag);
        var handler = new RejectSnagCommandHandler(repo, new FakeUnitOfWork(), new FakeClock(DateTimeOffset.UtcNow));

        await handler.HandleAsync(
            new RejectSnagCommand(snag.Id, "U999999", "Bob Maintenance", "Duplicate of an earlier report", snag.Version),
            default);

        var stored = repo.Store[snag.Id];
        Assert.Equal(SnagStatus.Rejected, stored.Status);
        var comment = Assert.Single(stored.Comments);
        Assert.Equal("Duplicate of an earlier report", comment.Body);
        Assert.Equal("U999999", comment.AuthorStaffId);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/SnagList.Application.Tests --filter "ChangeSnagStatusCommandTests|RejectSnagCommandTests"`
Expected: FAIL — the command/handler types don't exist yet.

- [ ] **Step 3: Implement**

```csharp
namespace SnagList.Application.Snags.Commands;

using SnagList.Application.Abstractions;
using SnagList.Application.Snags;
using SnagList.Domain.Snags;

public sealed record ChangeSnagStatusCommand(Guid SnagId, SnagStatus TargetStatus, string ActingStaffId, int ExpectedVersion);

public sealed class ChangeSnagStatusCommandHandler(ISnagRepository repository, IUnitOfWork unitOfWork, IClock clock)
{
    public async Task HandleAsync(ChangeSnagStatusCommand command, CancellationToken ct)
    {
        var snag = await repository.GetAsync(command.SnagId, ct)
            ?? throw new SnagNotFoundException(command.SnagId);
        if (snag.Version != command.ExpectedVersion)
        {
            throw new SnagVersionConflictException(snag.Id, command.ExpectedVersion, snag.Version);
        }

        snag.TransitionTo(command.TargetStatus, command.ActingStaffId, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
```

```csharp
namespace SnagList.Application.Snags.Commands;

using SnagList.Application.Abstractions;
using SnagList.Application.Snags;
using SnagList.Domain.Snags;

public sealed record RejectSnagCommand(
    Guid SnagId, string ActingStaffId, string ActingStaffName, string Reason, int ExpectedVersion);

public sealed class RejectSnagCommandHandler(ISnagRepository repository, IUnitOfWork unitOfWork, IClock clock)
{
    public async Task HandleAsync(RejectSnagCommand command, CancellationToken ct)
    {
        var snag = await repository.GetAsync(command.SnagId, ct)
            ?? throw new SnagNotFoundException(command.SnagId);
        if (snag.Version != command.ExpectedVersion)
        {
            throw new SnagVersionConflictException(snag.Id, command.ExpectedVersion, snag.Version);
        }

        snag.AddComment(command.ActingStaffId, command.ActingStaffName, command.Reason, clock.UtcNow);
        snag.TransitionTo(SnagStatus.Rejected, command.ActingStaffId, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/SnagList.Application.Tests --filter "ChangeSnagStatusCommandTests|RejectSnagCommandTests"`
Expected: PASS (3 + 1 tests).

- [ ] **Step 5: Commit**

```bash
git add src/SnagList.Application/Snags/Commands tests/SnagList.Application.Tests/Snags/ChangeSnagStatusCommandTests.cs \
  tests/SnagList.Application.Tests/Snags/RejectSnagCommandTests.cs
git commit -m "feat(application): add Maintenance status-transition and Reject commands"
```

---

## Task 9: Application — `AddSnagComment` and `UploadSnagPhoto` commands

**Design note:** neither command takes `expectedVersion`. Both are purely additive — a comment
never overwrites another comment, and a photo upload's real precondition is "the `Snag` is still
`Reported`," which `Snag.AddPhoto`'s own `EnsureEditable()` check already enforces from current
state, not from a client-supplied version number. `expectedVersion` (Tasks 7–8) exists to protect
*overwriting* mutations (`Edit`, a status transition); it would only add friction here, rejecting a
perfectly fine comment just because someone else's comment landed a moment earlier.

**Files:**
- Create: `src/SnagList.Application/Snags/Commands/AddSnagCommentCommand.cs`
- Create: `src/SnagList.Application/Snags/Commands/UploadSnagPhotoCommand.cs`
- Test: `tests/SnagList.Application.Tests/Testing/FakeBlobStorage.cs` (test double)
- Test: `tests/SnagList.Application.Tests/Snags/AddSnagCommentCommandTests.cs`
- Test: `tests/SnagList.Application.Tests/Snags/UploadSnagPhotoCommandTests.cs`

**Interfaces:**
- Consumes: `ISnagRepository`, `IUnitOfWork`, `IClock`, `IBlobStorage` (Task 5); `snag.AddComment`,
  `snag.AddPhoto` (Task 3).
- Produces: `AddSnagCommentCommandHandler.HandleAsync(...)`,
  `UploadSnagPhotoCommandHandler.HandleAsync(...)` — consumed by the API endpoints in Task 19.

- [ ] **Step 1: Write the failing tests**

```csharp
namespace SnagList.Application.Tests.Testing;

using SnagList.Application.Abstractions;

public sealed class FakeBlobStorage : IBlobStorage
{
    public readonly List<(string Key, string ContentType)> PutCalls = [];

    public Task PutAsync(string key, Stream content, string contentType, CancellationToken ct)
    {
        PutCalls.Add((key, contentType));
        return Task.CompletedTask;
    }

    public Task<Stream> GetAsync(string key, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream());

    public Task<Uri> GetPresignedGetUrlAsync(string key, TimeSpan expiry, CancellationToken ct) =>
        Task.FromResult(new Uri($"https://fake-storage.test/{key}"));

    public Task DeleteAsync(string key, CancellationToken ct) => Task.CompletedTask;
}
```

```csharp
namespace SnagList.Application.Tests.Snags;

using SnagList.Application.Snags.Commands;
using SnagList.Application.Tests.Testing;
using SnagList.Domain.Snags;
using Xunit;

public class AddSnagCommentCommandTests
{
    [Fact]
    public async Task Adds_a_comment_regardless_of_status()
    {
        var snag = Snag.Report(
            Guid.NewGuid(), "3rd floor", SnagCategory.Other, SnagSeverity.Low,
            "Squeaky door", "U123456", "Jane Smith", DateTimeOffset.UtcNow);
        snag.TransitionTo(SnagStatus.Acknowledged, "U999999", DateTimeOffset.UtcNow);
        var repo = new FakeSnagRepository();
        repo.Add(snag);
        var handler = new AddSnagCommentCommandHandler(repo, new FakeUnitOfWork(), new FakeClock(DateTimeOffset.UtcNow));

        await handler.HandleAsync(
            new AddSnagCommentCommand(snag.Id, "U999999", "Bob Maintenance", "Parts ordered"), default);

        Assert.Equal("Parts ordered", Assert.Single(repo.Store[snag.Id].Comments).Body);
    }
}
```

```csharp
namespace SnagList.Application.Tests.Snags;

using SnagList.Application.Snags.Commands;
using SnagList.Application.Tests.Testing;
using SnagList.Domain.Snags;
using Xunit;

public class UploadSnagPhotoCommandTests
{
    [Fact]
    public async Task Uploads_bytes_to_blob_storage_and_attaches_the_photo()
    {
        var snag = Snag.Report(
            Guid.NewGuid(), "3rd floor", SnagCategory.Electrical, SnagSeverity.Medium,
            "Flickering light", "U123456", "Jane Smith", DateTimeOffset.UtcNow);
        var repo = new FakeSnagRepository();
        repo.Add(snag);
        var blobStorage = new FakeBlobStorage();
        var handler = new UploadSnagPhotoCommandHandler(repo, blobStorage, new FakeUnitOfWork(), new FakeClock(DateTimeOffset.UtcNow));
        using var content = new MemoryStream([1, 2, 3]);

        await handler.HandleAsync(
            new UploadSnagPhotoCommand(snag.Id, "light.jpg", "image/jpeg", content, 3), default);

        var photo = Assert.Single(repo.Store[snag.Id].Photos);
        Assert.Equal("light.jpg", photo.FileName);
        Assert.StartsWith($"snags/{snag.Id}/", photo.BlobKey);
        Assert.Single(blobStorage.PutCalls);
        Assert.Equal("image/jpeg", blobStorage.PutCalls[0].ContentType);
    }

    [Fact]
    public async Task Throws_SnagPhotoLimitExceededException_on_the_sixth_photo()
    {
        var snag = Snag.Report(
            Guid.NewGuid(), "3rd floor", SnagCategory.Electrical, SnagSeverity.Medium,
            "Flickering light", "U123456", "Jane Smith", DateTimeOffset.UtcNow);
        var repo = new FakeSnagRepository();
        repo.Add(snag);
        var handler = new UploadSnagPhotoCommandHandler(repo, new FakeBlobStorage(), new FakeUnitOfWork(), new FakeClock(DateTimeOffset.UtcNow));
        for (var i = 0; i < 5; i++)
        {
            using var c = new MemoryStream([1]);
            await handler.HandleAsync(new UploadSnagPhotoCommand(snag.Id, $"p{i}.jpg", "image/jpeg", c, 1), default);
        }

        using var sixth = new MemoryStream([1]);
        await Assert.ThrowsAsync<SnagPhotoLimitExceededException>(
            () => handler.HandleAsync(new UploadSnagPhotoCommand(snag.Id, "p6.jpg", "image/jpeg", sixth, 1), default));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/SnagList.Application.Tests --filter "AddSnagCommentCommandTests|UploadSnagPhotoCommandTests"`
Expected: FAIL — the command/handler types don't exist yet.

- [ ] **Step 3: Implement**

```csharp
namespace SnagList.Application.Snags.Commands;

using SnagList.Application.Abstractions;
using SnagList.Application.Snags;

public sealed record AddSnagCommentCommand(Guid SnagId, string AuthorStaffId, string AuthorName, string Body);

public sealed class AddSnagCommentCommandHandler(ISnagRepository repository, IUnitOfWork unitOfWork, IClock clock)
{
    public async Task HandleAsync(AddSnagCommentCommand command, CancellationToken ct)
    {
        var snag = await repository.GetAsync(command.SnagId, ct)
            ?? throw new SnagNotFoundException(command.SnagId);
        snag.AddComment(command.AuthorStaffId, command.AuthorName, command.Body, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
```

```csharp
namespace SnagList.Application.Snags.Commands;

using SnagList.Application.Abstractions;
using SnagList.Application.Snags;
using SnagList.Domain.Snags;

public sealed record UploadSnagPhotoCommand(Guid SnagId, string FileName, string ContentType, Stream Content, long SizeBytes);

public sealed class UploadSnagPhotoCommandHandler(
    ISnagRepository repository, IBlobStorage blobStorage, IUnitOfWork unitOfWork, IClock clock)
{
    public async Task HandleAsync(UploadSnagPhotoCommand command, CancellationToken ct)
    {
        var snag = await repository.GetAsync(command.SnagId, ct)
            ?? throw new SnagNotFoundException(command.SnagId);

        var blobKey = $"snags/{command.SnagId}/{Guid.NewGuid()}-{command.FileName}";
        await blobStorage.PutAsync(blobKey, command.Content, command.ContentType, ct);

        snag.AddPhoto(new SnagPhoto(blobKey, command.FileName, command.ContentType, command.SizeBytes, clock.UtcNow));
        await unitOfWork.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/SnagList.Application.Tests --filter "AddSnagCommentCommandTests|UploadSnagPhotoCommandTests"`
Expected: PASS (1 + 2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/SnagList.Application/Snags/Commands/AddSnagCommentCommand.cs \
  src/SnagList.Application/Snags/Commands/UploadSnagPhotoCommand.cs \
  tests/SnagList.Application.Tests/Snags/AddSnagCommentCommandTests.cs \
  tests/SnagList.Application.Tests/Snags/UploadSnagPhotoCommandTests.cs \
  tests/SnagList.Application.Tests/Testing/FakeBlobStorage.cs
git commit -m "feat(application): add AddSnagComment and UploadSnagPhoto commands"
```

---

## Task 10: Application — `ListSnags` and `GetSnag` queries

Completes the Application layer. Like `ListLocationsQueryHandler` (Task 5), both handlers here are
pure passthroughs to `ISnagQueries` — the actual query logic (and its test coverage) lives in the
EF-backed implementation, Task 12, and is exercised end-to-end by the API integration tests in
Task 18.

**Files:**
- Create: `src/SnagList.Application/Snags/Queries/ListSnagsQuery.cs`
- Create: `src/SnagList.Application/Snags/Queries/GetSnagQuery.cs`
- Create: `src/SnagList.Application/Abstractions/ISnagQueries.cs`

**Interfaces:**
- Produces: `ListSnagsQueryHandler.HandleAsync(ListSnagsQuery, ct) -> CursorPage<SnagSummary>`,
  `GetSnagQueryHandler.HandleAsync(GetSnagQuery, ct) -> SnagDetail?` — consumed by the API endpoints
  in Task 18. `ISnagQueries` — implemented by `EfSnagQueries` in Task 12.

- [ ] **Step 1: Write `ListSnagsQuery`**

```csharp
namespace SnagList.Application.Snags.Queries;

using SnagList.Domain.Snags;

public sealed record ListSnagsQuery(
    Guid? LocationId, SnagCategory? Category, SnagSeverity? Severity, SnagStatus? Status,
    string? Cursor, int Limit);

public sealed record SnagSummary(
    Guid Id, Guid LocationId, string SubLocation, SnagCategory Category, SnagSeverity Severity,
    SnagStatus Status, string ReportedByName, DateTimeOffset ReportedAt, int Version);

public sealed record SnagListCursorKey(DateTimeOffset ReportedAt, Guid Id);
```

- [ ] **Step 2: Write `GetSnagQuery`**

```csharp
namespace SnagList.Application.Snags.Queries;

using SnagList.Domain.Snags;

public sealed record GetSnagQuery(Guid SnagId);

public sealed record SnagCommentDto(Guid Id, string AuthorStaffId, string AuthorName, string Body, DateTimeOffset CreatedAt);

public sealed record SnagPhotoDto(string BlobKey, string FileName, string ContentType, long SizeBytes, DateTimeOffset UploadedAt);

public sealed record SnagDetail(
    Guid Id, Guid LocationId, string SubLocation, SnagCategory Category, SnagSeverity Severity,
    string Description, SnagStatus Status, string ReportedByStaffId, string ReportedByName,
    DateTimeOffset ReportedAt, int Version,
    IReadOnlyList<SnagCommentDto> Comments, IReadOnlyList<SnagPhotoDto> Photos);
```

- [ ] **Step 3: Write the port and the two passthrough handlers**

```csharp
namespace SnagList.Application.Abstractions;

using SnagList.Application.Common;
using SnagList.Application.Snags.Queries;

public interface ISnagQueries
{
    Task<CursorPage<SnagSummary>> ListAsync(ListSnagsQuery query, CancellationToken ct);
    Task<SnagDetail?> GetAsync(Guid id, CancellationToken ct);
}
```

```csharp
namespace SnagList.Application.Snags.Queries;

using SnagList.Application.Abstractions;
using SnagList.Application.Common;

public sealed class ListSnagsQueryHandler(ISnagQueries queries)
{
    public Task<CursorPage<SnagSummary>> HandleAsync(ListSnagsQuery query, CancellationToken ct) =>
        queries.ListAsync(query, ct);
}

public sealed class GetSnagQueryHandler(ISnagQueries queries)
{
    public Task<SnagDetail?> HandleAsync(GetSnagQuery query, CancellationToken ct) =>
        queries.GetAsync(query.SnagId, ct);
}
```

- [ ] **Step 4: Build to confirm the Application layer compiles clean**

Run: `dotnet build src/SnagList.Application`
Expected: succeeds with no warnings (the project has `TreatWarningsAsErrors`).

- [ ] **Step 5: Run the full Application test suite**

Run: `dotnet test tests/SnagList.Application.Tests`
Expected: PASS — every test from Tasks 5–9 is green. This is the Application layer's exit gate
before Infrastructure (Task 11) starts implementing its ports.

- [ ] **Step 6: Commit**

```bash
git add src/SnagList.Application/Snags/Queries src/SnagList.Application/Abstractions/ISnagQueries.cs
git commit -m "feat(application): add ListSnags and GetSnag queries"
```

---

## Task 11: Infrastructure — EF Core `DbContext`, entity configurations, and the initial migration

EF mapping configuration has no meaningful unit test in isolation — a mapping bug only surfaces
against a real database. This task's verification is `dotnet build` plus a successful
`dotnet ef migrations add`; the configurations themselves are validated for real by Task 12's
Testcontainers-backed repository tests, which is where TDD resumes.

**Files:**
- Create: `src/SnagList.Infrastructure/Persistence/SnagListDbContext.cs`
- Create: `src/SnagList.Infrastructure/Persistence/Configurations/LocationConfiguration.cs`
- Create: `src/SnagList.Infrastructure/Persistence/Configurations/SnagConfiguration.cs`
- Create: `src/SnagList.Infrastructure/Persistence/Configurations/SnagCommentConfiguration.cs`
- Create: `src/SnagList.Infrastructure/Persistence/Configurations/StaffIdentityConfiguration.cs`
- Create: `src/SnagList.Infrastructure/Audit/AuditLogEntry.cs`
- Create: `src/SnagList.Infrastructure/Persistence/Configurations/AuditLogEntryConfiguration.cs`
- Create: `src/SnagList.Infrastructure/Migrations/*` (generated)

**Interfaces:**
- Consumes: `Location`, `Snag`, `SnagComment`, `SnagPhoto`, `StaffIdentity`, `StaffRole` (Tasks 2–4).
- Produces: `SnagListDbContext` — consumed by every repository/query implementation in Task 12 and
  by `SnagList.SeedData` in Task 21.

- [ ] **Step 1: Add the EF Core packages**

```bash
dotnet add src/SnagList.Infrastructure package Microsoft.EntityFrameworkCore
dotnet add src/SnagList.Infrastructure package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src/SnagList.Infrastructure package Microsoft.EntityFrameworkCore.Design
dotnet tool install --global dotnet-ef --version 10.*
```

- [ ] **Step 2: Write `AuditLogEntry`**

Not a domain concept (see `ontology.config.json`'s existing rationale for keeping infrastructure
concerns like this out of `docs/ontology.ttl`) — a plain persistence record for `IAuditWriter`.

```csharp
namespace SnagList.Infrastructure.Audit;

public sealed class AuditLogEntry
{
    public Guid Id { get; private set; }
    public string ActorStaffId { get; private set; } = "";
    public string Action { get; private set; } = "";
    public string EntityType { get; private set; } = "";
    public Guid EntityId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    private AuditLogEntry() { } // EF Core

    public AuditLogEntry(string actorStaffId, string action, string entityType, Guid entityId, DateTimeOffset occurredAt)
    {
        Id = Guid.NewGuid();
        ActorStaffId = actorStaffId;
        Action = action;
        EntityType = entityType;
        EntityId = entityId;
        OccurredAt = occurredAt;
    }
}
```

- [ ] **Step 3: Write the entity configurations**

```csharp
namespace SnagList.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SnagList.Domain.Locations;

public sealed class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.ToTable("locations");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Name).HasMaxLength(200).IsRequired();
        builder.Property(l => l.Address).HasMaxLength(500).IsRequired();
        builder.Property(l => l.IsActive).IsRequired();
    }
}
```

```csharp
namespace SnagList.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SnagList.Domain.Snags;

public sealed class SnagConfiguration : IEntityTypeConfiguration<Snag>
{
    public void Configure(EntityTypeBuilder<Snag> builder)
    {
        builder.ToTable("snags");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.SubLocation).HasMaxLength(300).IsRequired();
        builder.Property(s => s.Description).HasMaxLength(4000).IsRequired();
        builder.Property(s => s.ReportedByStaffId).HasMaxLength(50).IsRequired();
        builder.Property(s => s.ReportedByName).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Category).HasConversion<string>().HasMaxLength(50);
        builder.Property(s => s.Severity).HasConversion<string>().HasMaxLength(50);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(50);
        builder.Property(s => s.Version).IsConcurrencyToken();

        builder.HasIndex(s => s.LocationId);
        builder.HasIndex(s => s.Status);

        builder.OwnsMany(s => s.Photos, photo =>
        {
            photo.ToTable("snag_photos");
            photo.WithOwner().HasForeignKey("snag_id");
            photo.Property<Guid>("id").ValueGeneratedOnAdd();
            photo.HasKey("id");
            photo.Property(p => p.BlobKey).HasColumnName("blob_key").IsRequired();
            photo.Property(p => p.FileName).HasColumnName("file_name").IsRequired();
            photo.Property(p => p.ContentType).HasColumnName("content_type").IsRequired();
            photo.Property(p => p.SizeBytes).HasColumnName("size_bytes");
            photo.Property(p => p.UploadedAt).HasColumnName("uploaded_at");
        });
        builder.Navigation(s => s.Photos).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(s => s.Comments)
            .WithOne()
            .HasForeignKey(c => c.SnagId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(s => s.Comments).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
```

```csharp
namespace SnagList.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SnagList.Domain.Snags;

public sealed class SnagCommentConfiguration : IEntityTypeConfiguration<SnagComment>
{
    public void Configure(EntityTypeBuilder<SnagComment> builder)
    {
        builder.ToTable("snag_comments");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.AuthorStaffId).HasMaxLength(50).IsRequired();
        builder.Property(c => c.AuthorName).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Body).HasMaxLength(4000).IsRequired();
    }
}
```

```csharp
namespace SnagList.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SnagList.Domain.Staff;

public sealed class StaffIdentityConfiguration : IEntityTypeConfiguration<StaffIdentity>
{
    public void Configure(EntityTypeBuilder<StaffIdentity> builder)
    {
        builder.ToTable("staff_identities");
        builder.HasKey(s => s.StaffId);
        builder.Property(s => s.StaffId).HasMaxLength(50);
        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Email).HasMaxLength(300).IsRequired();

        var rolesProperty = builder.Property(s => s.Roles).HasConversion(
            roles => string.Join(',', roles),
            value => value.Length == 0
                ? Array.Empty<StaffRole>()
                : value.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Enum.Parse<StaffRole>).ToArray());
        rolesProperty.Metadata.SetValueComparer(new ValueComparer<IReadOnlyList<StaffRole>>(
            (a, b) => a!.SequenceEqual(b!),
            a => a.Aggregate(0, (hash, role) => HashCode.Combine(hash, role)),
            a => a.ToList()));
    }
}
```

```csharp
namespace SnagList.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SnagList.Infrastructure.Audit;

public sealed class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.ToTable("audit_log_entries");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.ActorStaffId).HasMaxLength(50).IsRequired();
        builder.Property(a => a.Action).HasMaxLength(100).IsRequired();
        builder.Property(a => a.EntityType).HasMaxLength(100).IsRequired();
    }
}
```

- [ ] **Step 4: Write `SnagListDbContext`**

```csharp
namespace SnagList.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using SnagList.Domain.Locations;
using SnagList.Domain.Snags;
using SnagList.Domain.Staff;
using SnagList.Infrastructure.Audit;

public sealed class SnagListDbContext(DbContextOptions<SnagListDbContext> options) : DbContext(options)
{
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Snag> Snags => Set<Snag>();
    public DbSet<StaffIdentity> StaffIdentities => Set<StaffIdentity>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SnagListDbContext).Assembly);
    }
}
```

- [ ] **Step 5: Build, then generate the initial migration**

Run: `dotnet build src/SnagList.Infrastructure`
Expected: succeeds.

Run (from repo root, with a design-time factory or `--project`/`--startup-project` pointed at
`SnagList.Api` once Task 16+ gives it a `Program.cs` that registers `SnagListDbContext` — until
then, add a temporary design-time `IDesignTimeDbContextFactory<SnagListDbContext>` in
`src/SnagList.Infrastructure/Persistence/DesignTimeDbContextFactory.cs` pointed at a placeholder
local connection string, used only by tooling, never by the running app):

```csharp
namespace SnagList.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SnagListDbContext>
{
    public SnagListDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<SnagListDbContext>();
        builder.UseNpgsql("Host=localhost;Database=snaglist;Username=postgres;Password=postgres");
        return new SnagListDbContext(builder.Options);
    }
}
```

```bash
dotnet ef migrations add InitialCreate --project src/SnagList.Infrastructure
```

Expected: a `Migrations/` folder is generated under `src/SnagList.Infrastructure` with tables for
`locations`, `snags`, `snag_photos`, `snag_comments`, `staff_identities`, `audit_log_entries`.

- [ ] **Step 6: Commit**

```bash
git add src/SnagList.Infrastructure
git commit -m "feat(infrastructure): add EF Core DbContext, configurations, and initial migration"
```

---

## Task 12: Infrastructure — repositories, unit of work, and read queries (Testcontainers)

TDD resumes here against a real Postgres instance via Testcontainers — no mocks, per
08-testing-and-nonfunctional.md.

**Files:**
- Create: `src/SnagList.Infrastructure/Persistence/Repositories/EfLocationRepository.cs`
- Create: `src/SnagList.Infrastructure/Persistence/Repositories/EfSnagRepository.cs`
- Create: `src/SnagList.Infrastructure/Persistence/EfUnitOfWork.cs`
- Create: `src/SnagList.Infrastructure/Persistence/Queries/EfLocationQueries.cs`
- Create: `src/SnagList.Infrastructure/Persistence/Queries/EfSnagQueries.cs`
- Test: `tests/SnagList.Infrastructure.Tests/Testing/PostgresFixture.cs`
- Test: `tests/SnagList.Infrastructure.Tests/Persistence/EfLocationRepositoryTests.cs`
- Test: `tests/SnagList.Infrastructure.Tests/Persistence/EfSnagRepositoryTests.cs`
- Test: `tests/SnagList.Infrastructure.Tests/Persistence/EfUnitOfWorkTests.cs`
- Test: `tests/SnagList.Infrastructure.Tests/Persistence/EfLocationQueriesTests.cs`
- Test: `tests/SnagList.Infrastructure.Tests/Persistence/EfSnagQueriesTests.cs`

**Interfaces:**
- Consumes: `SnagListDbContext` (Task 11); `ILocationRepository`, `ISnagRepository`,
  `IUnitOfWork`, `ILocationQueries`, `ISnagQueries` (Task 5, Task 10).
- Produces: concrete implementations registered in `Program.cs` (Task 16); no new interfaces.

- [ ] **Step 1: Add the Testcontainers package and write the shared fixture**

```bash
dotnet add tests/SnagList.Infrastructure.Tests package Testcontainers.PostgreSql
```

```csharp
namespace SnagList.Infrastructure.Tests.Testing;

using Microsoft.EntityFrameworkCore;
using SnagList.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public SnagListDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<SnagListDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;
        return new SnagListDbContext(options);
    }
}

[CollectionDefinition("Postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
```

- [ ] **Step 2: Write the failing repository round-trip tests**

```csharp
namespace SnagList.Infrastructure.Tests.Persistence;

using SnagList.Domain.Locations;
using SnagList.Infrastructure.Persistence.Repositories;
using SnagList.Infrastructure.Tests.Testing;
using Xunit;

[Collection("Postgres")]
public class EfLocationRepositoryTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Add_then_GetAsync_round_trips_a_Location()
    {
        await using var writeContext = fixture.CreateContext();
        var location = Location.Create("Head Office", "1 Main St");
        new EfLocationRepository(writeContext).Add(location);
        await writeContext.SaveChangesAsync();

        await using var readContext = fixture.CreateContext();
        var loaded = await new EfLocationRepository(readContext).GetAsync(location.Id, default);

        Assert.NotNull(loaded);
        Assert.Equal("Head Office", loaded!.Name);
        Assert.True(loaded.IsActive);
    }

    [Fact]
    public async Task GetAsync_returns_null_for_an_unknown_id()
    {
        await using var context = fixture.CreateContext();
        var loaded = await new EfLocationRepository(context).GetAsync(Guid.NewGuid(), default);
        Assert.Null(loaded);
    }
}
```

```csharp
namespace SnagList.Infrastructure.Tests.Persistence;

using SnagList.Domain.Locations;
using SnagList.Domain.Snags;
using SnagList.Infrastructure.Persistence.Repositories;
using SnagList.Infrastructure.Tests.Testing;
using Xunit;

[Collection("Postgres")]
public class EfSnagRepositoryTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Add_then_GetAsync_round_trips_a_Snag_with_photos_and_comments()
    {
        await using var writeContext = fixture.CreateContext();
        var location = Location.Create("Head Office", "1 Main St");
        writeContext.Locations.Add(location);
        var snag = Snag.Report(
            location.Id, "3rd floor", SnagCategory.Electrical, SnagSeverity.Medium,
            "Flickering light", "U123456", "Jane Smith", DateTimeOffset.UtcNow);
        snag.AddPhoto(new SnagPhoto("blob-1", "photo1.jpg", "image/jpeg", 2048, DateTimeOffset.UtcNow));
        snag.AddComment("U999999", "Bob Maintenance", "Looking into it", DateTimeOffset.UtcNow);
        new EfSnagRepository(writeContext).Add(snag);
        await writeContext.SaveChangesAsync();

        await using var readContext = fixture.CreateContext();
        var loaded = await new EfSnagRepository(readContext).GetAsync(snag.Id, default);

        Assert.NotNull(loaded);
        Assert.Single(loaded!.Photos);
        Assert.Equal("photo1.jpg", loaded.Photos[0].FileName);
        Assert.Single(loaded.Comments);
        Assert.Equal("Looking into it", loaded.Comments[0].Body);
    }
}
```

```csharp
namespace SnagList.Infrastructure.Tests.Persistence;

using SnagList.Application.Snags;
using SnagList.Domain.Locations;
using SnagList.Domain.Snags;
using SnagList.Infrastructure.Persistence;
using SnagList.Infrastructure.Tests.Testing;
using Xunit;

[Collection("Postgres")]
public class EfUnitOfWorkTests(PostgresFixture fixture)
{
    [Fact]
    public async Task SaveChangesAsync_translates_a_real_concurrency_conflict_into_SnagVersionConflictException()
    {
        await using var setupContext = fixture.CreateContext();
        var location = Location.Create("Head Office", "1 Main St");
        setupContext.Locations.Add(location);
        var snag = Snag.Report(
            location.Id, "3rd floor", SnagCategory.Electrical, SnagSeverity.Low,
            "desc", "U1", "Jane", DateTimeOffset.UtcNow);
        setupContext.Snags.Add(snag);
        await setupContext.SaveChangesAsync();

        await using var contextA = fixture.CreateContext();
        await using var contextB = fixture.CreateContext();
        var snagA = await contextA.Snags.FirstAsync(s => s.Id == snag.Id);
        var snagB = await contextB.Snags.FirstAsync(s => s.Id == snag.Id);

        snagA.Edit("4th floor", SnagCategory.Electrical, SnagSeverity.Low, "edited by A");
        await new EfUnitOfWork(contextA).SaveChangesAsync(default);

        snagB.Edit("5th floor", SnagCategory.Electrical, SnagSeverity.Low, "edited by B, stale");
        var ex = await Assert.ThrowsAsync<SnagVersionConflictException>(
            () => new EfUnitOfWork(contextB).SaveChangesAsync(default));

        Assert.Equal(snag.Id, ex.SnagId);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/SnagList.Infrastructure.Tests --filter "EfLocationRepositoryTests|EfSnagRepositoryTests|EfUnitOfWorkTests"`
Expected: FAIL — `EfLocationRepository`, `EfSnagRepository`, `EfUnitOfWork` don't exist. (Requires
Docker running locally for Testcontainers to start the Postgres container.)

- [ ] **Step 4: Implement the repositories and unit of work**

```csharp
namespace SnagList.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using SnagList.Application.Abstractions;
using SnagList.Domain.Locations;

public sealed class EfLocationRepository(SnagListDbContext dbContext) : ILocationRepository
{
    public Task<Location?> GetAsync(Guid id, CancellationToken ct) =>
        dbContext.Locations.FirstOrDefaultAsync(l => l.Id == id, ct);

    public void Add(Location location) => dbContext.Locations.Add(location);
}
```

```csharp
namespace SnagList.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using SnagList.Application.Abstractions;
using SnagList.Domain.Snags;

public sealed class EfSnagRepository(SnagListDbContext dbContext) : ISnagRepository
{
    public Task<Snag?> GetAsync(Guid id, CancellationToken ct) =>
        dbContext.Snags
            .Include(s => s.Photos)
            .Include(s => s.Comments)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public void Add(Snag snag) => dbContext.Snags.Add(snag);
}
```

```csharp
namespace SnagList.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using SnagList.Application.Abstractions;
using SnagList.Application.Snags;
using SnagList.Domain.Snags;

public sealed class EfUnitOfWork(SnagListDbContext dbContext) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            var entry = ex.Entries.Single();
            if (entry.Entity is not Snag snag) throw;

            var expectedVersion = (int)entry.OriginalValues["Version"]!;
            var databaseValues = await entry.GetDatabaseValuesAsync(ct);
            var actualVersion = databaseValues is null ? expectedVersion : (int)databaseValues["Version"]!;
            throw new SnagVersionConflictException(snag.Id, expectedVersion, actualVersion);
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/SnagList.Infrastructure.Tests --filter "EfLocationRepositoryTests|EfSnagRepositoryTests|EfUnitOfWorkTests"`
Expected: PASS (2 + 1 + 1 tests).

- [ ] **Step 6: Write the failing query tests**

```csharp
namespace SnagList.Infrastructure.Tests.Persistence;

using SnagList.Application.Locations.Queries;
using SnagList.Domain.Locations;
using SnagList.Infrastructure.Persistence.Queries;
using SnagList.Infrastructure.Tests.Testing;
using Xunit;

[Collection("Postgres")]
public class EfLocationQueriesTests(PostgresFixture fixture)
{
    [Fact]
    public async Task ListAsync_pages_by_name_and_excludes_retired_by_default()
    {
        await using var context = fixture.CreateContext();
        var active1 = Location.Create("Engineering Site", "3 Park Rd");
        var active2 = Location.Create("Head Office", "1 Main St");
        var retired = Location.Create("Old Depot", "9 Yard Ln");
        retired.Retire();
        context.Locations.AddRange(active1, active2, retired);
        await context.SaveChangesAsync();

        var queries = new EfLocationQueries(context);
        var firstPage = await queries.ListAsync(new ListLocationsQuery(IncludeRetired: false, Cursor: null, Limit: 1), default);

        Assert.Single(firstPage.Items);
        Assert.Equal("Engineering Site", firstPage.Items[0].Name); // alphabetically first
        Assert.NotNull(firstPage.NextCursor);

        var secondPage = await queries.ListAsync(
            new ListLocationsQuery(IncludeRetired: false, Cursor: firstPage.NextCursor, Limit: 1), default);
        Assert.Single(secondPage.Items);
        Assert.Equal("Head Office", secondPage.Items[0].Name);
        Assert.Null(secondPage.NextCursor);
    }
}
```

```csharp
namespace SnagList.Infrastructure.Tests.Persistence;

using SnagList.Application.Snags.Queries;
using SnagList.Domain.Locations;
using SnagList.Domain.Snags;
using SnagList.Infrastructure.Persistence.Queries;
using SnagList.Infrastructure.Tests.Testing;
using Xunit;

[Collection("Postgres")]
public class EfSnagQueriesTests(PostgresFixture fixture)
{
    [Fact]
    public async Task ListAsync_pages_newest_first_and_filters_by_status()
    {
        await using var context = fixture.CreateContext();
        var location = Location.Create("Head Office", "1 Main St");
        context.Locations.Add(location);
        var older = Snag.Report(location.Id, "3rd floor", SnagCategory.Electrical, SnagSeverity.Low,
            "older", "U1", "Jane", DateTimeOffset.UtcNow.AddHours(-2));
        var newer = Snag.Report(location.Id, "4th floor", SnagCategory.Plumbing, SnagSeverity.Low,
            "newer", "U1", "Jane", DateTimeOffset.UtcNow.AddHours(-1));
        newer.TransitionTo(SnagStatus.Acknowledged, "U9", DateTimeOffset.UtcNow);
        context.Snags.AddRange(older, newer);
        await context.SaveChangesAsync();

        var queries = new EfSnagQueries(context);
        var page = await queries.ListAsync(
            new ListSnagsQuery(LocationId: null, Category: null, Severity: null, Status: SnagStatus.Acknowledged, Cursor: null, Limit: 10),
            default);

        var summary = Assert.Single(page.Items);
        Assert.Equal(newer.Id, summary.Id);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task GetAsync_returns_comments_and_photos()
    {
        await using var context = fixture.CreateContext();
        var location = Location.Create("Head Office", "1 Main St");
        context.Locations.Add(location);
        var snag = Snag.Report(location.Id, "3rd floor", SnagCategory.Electrical, SnagSeverity.Low,
            "desc", "U1", "Jane", DateTimeOffset.UtcNow);
        snag.AddComment("U9", "Bob", "noted", DateTimeOffset.UtcNow);
        context.Snags.Add(snag);
        await context.SaveChangesAsync();

        var detail = await new EfSnagQueries(context).GetAsync(snag.Id, default);

        Assert.NotNull(detail);
        Assert.Single(detail!.Comments);
        Assert.Equal("noted", detail.Comments[0].Body);
    }

    [Fact]
    public async Task GetAsync_returns_null_for_an_unknown_id()
    {
        await using var context = fixture.CreateContext();
        var detail = await new EfSnagQueries(context).GetAsync(Guid.NewGuid(), default);
        Assert.Null(detail);
    }
}
```

- [ ] **Step 7: Run tests to verify they fail**

Run: `dotnet test tests/SnagList.Infrastructure.Tests --filter "EfLocationQueriesTests|EfSnagQueriesTests"`
Expected: FAIL — `EfLocationQueries`/`EfSnagQueries` don't exist.

- [ ] **Step 8: Implement the query read models**

```csharp
namespace SnagList.Infrastructure.Persistence.Queries;

using Microsoft.EntityFrameworkCore;
using SnagList.Application.Abstractions;
using SnagList.Application.Common;
using SnagList.Application.Locations.Queries;

public sealed class EfLocationQueries(SnagListDbContext dbContext) : ILocationQueries
{
    public async Task<CursorPage<LocationSummary>> ListAsync(ListLocationsQuery query, CancellationToken ct)
    {
        var cursorKey = OpaqueCursor.Decode<LocationListCursorKey>(query.Cursor);

        var q = dbContext.Locations.AsNoTracking().AsQueryable();
        if (!query.IncludeRetired) q = q.Where(l => l.IsActive);
        if (cursorKey is not null)
        {
            q = q.Where(l => l.Name.CompareTo(cursorKey.Name) > 0
                || (l.Name == cursorKey.Name && l.Id.CompareTo(cursorKey.Id) > 0));
        }

        var rows = await q.OrderBy(l => l.Name).ThenBy(l => l.Id)
            .Take(query.Limit + 1)
            .Select(l => new LocationSummary(l.Id, l.Name, l.Address, l.IsActive))
            .ToListAsync(ct);

        var hasMore = rows.Count > query.Limit;
        var page = hasMore ? rows.Take(query.Limit).ToList() : rows;
        var nextCursor = hasMore
            ? OpaqueCursor.Encode(new LocationListCursorKey(page[^1].Name, page[^1].Id))
            : null;
        return new CursorPage<LocationSummary>(page, nextCursor);
    }
}
```

```csharp
namespace SnagList.Infrastructure.Persistence.Queries;

using Microsoft.EntityFrameworkCore;
using SnagList.Application.Abstractions;
using SnagList.Application.Common;
using SnagList.Application.Snags.Queries;

public sealed class EfSnagQueries(SnagListDbContext dbContext) : ISnagQueries
{
    public async Task<CursorPage<SnagSummary>> ListAsync(ListSnagsQuery query, CancellationToken ct)
    {
        var cursorKey = OpaqueCursor.Decode<SnagListCursorKey>(query.Cursor);

        var q = dbContext.Snags.AsNoTracking().AsQueryable();
        if (query.LocationId is { } locationId) q = q.Where(s => s.LocationId == locationId);
        if (query.Category is { } category) q = q.Where(s => s.Category == category);
        if (query.Severity is { } severity) q = q.Where(s => s.Severity == severity);
        if (query.Status is { } status) q = q.Where(s => s.Status == status);
        if (cursorKey is not null)
        {
            q = q.Where(s => s.ReportedAt < cursorKey.ReportedAt
                || (s.ReportedAt == cursorKey.ReportedAt && s.Id.CompareTo(cursorKey.Id) < 0));
        }

        var rows = await q.OrderByDescending(s => s.ReportedAt).ThenByDescending(s => s.Id)
            .Take(query.Limit + 1)
            .Select(s => new SnagSummary(
                s.Id, s.LocationId, s.SubLocation, s.Category, s.Severity, s.Status,
                s.ReportedByName, s.ReportedAt, s.Version))
            .ToListAsync(ct);

        var hasMore = rows.Count > query.Limit;
        var page = hasMore ? rows.Take(query.Limit).ToList() : rows;
        var nextCursor = hasMore
            ? OpaqueCursor.Encode(new SnagListCursorKey(page[^1].ReportedAt, page[^1].Id))
            : null;
        return new CursorPage<SnagSummary>(page, nextCursor);
    }

    public async Task<SnagDetail?> GetAsync(Guid id, CancellationToken ct)
    {
        var snag = await dbContext.Snags.AsNoTracking()
            .Include(s => s.Photos)
            .Include(s => s.Comments)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
        if (snag is null) return null;

        return new SnagDetail(
            snag.Id, snag.LocationId, snag.SubLocation, snag.Category, snag.Severity, snag.Description,
            snag.Status, snag.ReportedByStaffId, snag.ReportedByName, snag.ReportedAt, snag.Version,
            snag.Comments
                .Select(c => new SnagCommentDto(c.Id, c.AuthorStaffId, c.AuthorName, c.Body, c.CreatedAt))
                .ToList(),
            snag.Photos
                .Select(p => new SnagPhotoDto(p.BlobKey, p.FileName, p.ContentType, p.SizeBytes, p.UploadedAt))
                .ToList());
    }
}
```

- [ ] **Step 9: Run tests to verify they pass**

Run: `dotnet test tests/SnagList.Infrastructure.Tests`
Expected: PASS — the full Infrastructure persistence suite is green.

- [ ] **Step 10: Commit**

```bash
git add src/SnagList.Infrastructure/Persistence tests/SnagList.Infrastructure.Tests
git commit -m "feat(infrastructure): add repositories, unit of work, and read queries"
```

---

## Task 13: Infrastructure — `IBlobStorage`, the single S3-API-compatible implementation (Testcontainers MinIO)

Per 02-solution-architecture.md, this is the *one* `IBlobStorage` implementation used against both
real S3 (AWS deployment) and MinIO (local/home-lab) — proven here against MinIO via Testcontainers.

**Files:**
- Create: `src/SnagList.Infrastructure/Storage/S3CompatibleBlobStorage.cs`
- Test: `tests/SnagList.Infrastructure.Tests/Testing/MinioFixture.cs`
- Test: `tests/SnagList.Infrastructure.Tests/Storage/S3CompatibleBlobStorageTests.cs`

**Interfaces:**
- Consumes: `IBlobStorage` (Task 5).
- Produces: `S3CompatibleBlobStorage` — registered against `IBlobStorage` in `Program.cs` (Task 16);
  consumed by `UploadSnagPhotoCommandHandler` (Task 9, already written against the interface) and
  by the photo-download endpoint (Task 19).

- [ ] **Step 1: Add packages and write the fixture**

```bash
dotnet add src/SnagList.Infrastructure package AWSSDK.S3
dotnet add tests/SnagList.Infrastructure.Tests package Testcontainers.Minio
```

```csharp
namespace SnagList.Infrastructure.Tests.Testing;

using Amazon.Runtime;
using Amazon.S3;
using Testcontainers.Minio;
using Xunit;

public sealed class MinioFixture : IAsyncLifetime
{
    public const string BucketName = "snaglist-photos-test";
    private readonly MinioContainer _container = new MinioBuilder().Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await CreateClient().PutBucketAsync(BucketName);
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public IAmazonS3 CreateClient() => new AmazonS3Client(
        new BasicAWSCredentials(_container.GetAccessKey(), _container.GetSecretKey()),
        new AmazonS3Config { ServiceURL = _container.GetConnectionString(), ForcePathStyle = true });
}

[CollectionDefinition("Minio")]
public sealed class MinioCollection : ICollectionFixture<MinioFixture>;
```

- [ ] **Step 2: Write the failing tests**

```csharp
namespace SnagList.Infrastructure.Tests.Storage;

using SnagList.Infrastructure.Storage;
using SnagList.Infrastructure.Tests.Testing;
using Xunit;

[Collection("Minio")]
public class S3CompatibleBlobStorageTests(MinioFixture fixture)
{
    [Fact]
    public async Task PutAsync_then_GetAsync_round_trips_bytes()
    {
        var storage = new S3CompatibleBlobStorage(fixture.CreateClient(), MinioFixture.BucketName);
        var original = new byte[] { 1, 2, 3, 4 };
        using var content = new MemoryStream(original);

        await storage.PutAsync("snags/test/photo.jpg", content, "image/jpeg", default);

        await using var readBack = await storage.GetAsync("snags/test/photo.jpg", default);
        using var buffer = new MemoryStream();
        await readBack.CopyToAsync(buffer);
        Assert.Equal(original, buffer.ToArray());
    }

    [Fact]
    public async Task GetPresignedGetUrlAsync_returns_a_url_pointing_at_the_key()
    {
        var storage = new S3CompatibleBlobStorage(fixture.CreateClient(), MinioFixture.BucketName);
        using var content = new MemoryStream([1]);
        await storage.PutAsync("snags/test/photo2.jpg", content, "image/jpeg", default);

        var url = await storage.GetPresignedGetUrlAsync("snags/test/photo2.jpg", TimeSpan.FromMinutes(5), default);

        Assert.Contains("photo2.jpg", url.ToString());
    }

    [Fact]
    public async Task DeleteAsync_removes_the_object()
    {
        var storage = new S3CompatibleBlobStorage(fixture.CreateClient(), MinioFixture.BucketName);
        using var content = new MemoryStream([1]);
        await storage.PutAsync("snags/test/photo3.jpg", content, "image/jpeg", default);

        await storage.DeleteAsync("snags/test/photo3.jpg", default);

        await Assert.ThrowsAnyAsync<Exception>(() => storage.GetAsync("snags/test/photo3.jpg", default));
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/SnagList.Infrastructure.Tests --filter S3CompatibleBlobStorageTests`
Expected: FAIL — `S3CompatibleBlobStorage` does not exist.

- [ ] **Step 4: Implement**

```csharp
namespace SnagList.Infrastructure.Storage;

using Amazon.S3;
using Amazon.S3.Model;
using SnagList.Application.Abstractions;

public sealed class S3CompatibleBlobStorage(IAmazonS3 s3Client, string bucketName) : IBlobStorage
{
    public Task PutAsync(string key, Stream content, string contentType, CancellationToken ct) =>
        s3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = key,
            InputStream = content,
            ContentType = contentType,
        }, ct);

    public async Task<Stream> GetAsync(string key, CancellationToken ct)
    {
        var response = await s3Client.GetObjectAsync(bucketName, key, ct);
        return response.ResponseStream;
    }

    public Task<Uri> GetPresignedGetUrlAsync(string key, TimeSpan expiry, CancellationToken ct) =>
        Task.FromResult(new Uri(s3Client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = bucketName,
            Key = key,
            Expires = DateTime.UtcNow.Add(expiry),
        })));

    public Task DeleteAsync(string key, CancellationToken ct) =>
        s3Client.DeleteObjectAsync(bucketName, key, ct);
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/SnagList.Infrastructure.Tests --filter S3CompatibleBlobStorageTests`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add src/SnagList.Infrastructure/Storage tests/SnagList.Infrastructure.Tests/Storage \
  tests/SnagList.Infrastructure.Tests/Testing/MinioFixture.cs
git commit -m "feat(infrastructure): add the S3-compatible IBlobStorage adapter"
```

---

## Task 14: Infrastructure — `IEmailSender` (SMTP), `IAuditWriter`, `IClock`

**Files:**
- Create: `src/SnagList.Infrastructure/Clock/SystemClock.cs`
- Create: `src/SnagList.Infrastructure/Audit/EfAuditWriter.cs`
- Create: `src/SnagList.Infrastructure/Email/SmtpEmailSenderOptions.cs`
- Create: `src/SnagList.Infrastructure/Email/SmtpEmailSender.cs`
- Test: `tests/SnagList.Infrastructure.Tests/Testing/MailpitFixture.cs`
- Test: `tests/SnagList.Infrastructure.Tests/Email/SmtpEmailSenderTests.cs`
- Test: `tests/SnagList.Infrastructure.Tests/Audit/EfAuditWriterTests.cs`

**Interfaces:**
- Consumes: `IClock`, `IEmailSender`, `IAuditWriter` (Task 5); `SnagListDbContext` (Task 11).
- Produces: `SystemClock`, `EfAuditWriter`, `SmtpEmailSender` — all registered in `Program.cs`
  (Task 16). `SmtpEmailSender` is what the notification wiring in Task 20 sends through.

`SystemClock` needs no test of its own — it's a one-line pass-through to `DateTimeOffset.UtcNow`
with nothing to assert beyond what the compiler already guarantees.

- [ ] **Step 1: Implement `SystemClock`**

```csharp
namespace SnagList.Infrastructure.Clock;

using SnagList.Application.Abstractions;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
```

- [ ] **Step 2: Write the failing `EfAuditWriter` test**

```csharp
namespace SnagList.Infrastructure.Tests.Audit;

using SnagList.Infrastructure.Audit;
using SnagList.Infrastructure.Tests.Testing;
using Xunit;

[Collection("Postgres")]
public class EfAuditWriterTests(PostgresFixture fixture)
{
    [Fact]
    public async Task WriteAsync_persists_an_audit_log_entry()
    {
        await using var context = fixture.CreateContext();
        var writer = new EfAuditWriter(context);
        var entityId = Guid.NewGuid();

        await writer.WriteAsync("U123456", "SnagReported", "Snag", entityId, DateTimeOffset.UtcNow, default);

        await using var readContext = fixture.CreateContext();
        var entry = await readContext.AuditLogEntries.SingleAsync(a => a.EntityId == entityId);
        Assert.Equal("U123456", entry.ActorStaffId);
        Assert.Equal("SnagReported", entry.Action);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/SnagList.Infrastructure.Tests --filter EfAuditWriterTests`
Expected: FAIL — `EfAuditWriter` does not exist.

- [ ] **Step 4: Implement `EfAuditWriter`**

```csharp
namespace SnagList.Infrastructure.Audit;

using SnagList.Application.Abstractions;
using SnagList.Infrastructure.Persistence;

public sealed class EfAuditWriter(SnagListDbContext dbContext) : IAuditWriter
{
    public async Task WriteAsync(
        string actorStaffId, string action, string entityType, Guid entityId,
        DateTimeOffset occurredAt, CancellationToken ct)
    {
        dbContext.AuditLogEntries.Add(new AuditLogEntry(actorStaffId, action, entityType, entityId, occurredAt));
        await dbContext.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/SnagList.Infrastructure.Tests --filter EfAuditWriterTests`
Expected: PASS.

- [ ] **Step 6: Add packages and write the Mailpit fixture**

```bash
dotnet add src/SnagList.Infrastructure package MailKit
dotnet add tests/SnagList.Infrastructure.Tests package Testcontainers
```

```csharp
namespace SnagList.Infrastructure.Tests.Testing;

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

public sealed class MailpitFixture : IAsyncLifetime
{
    private readonly IContainer _container = new ContainerBuilder()
        .WithImage("axllent/mailpit:latest")
        .WithPortBinding(1025, true)
        .WithPortBinding(8025, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(8025))
        .Build();

    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public string Hostname => _container.Hostname;
    public int SmtpPort => _container.GetMappedPublicPort(1025);
    public string HttpBaseUrl => $"http://{_container.Hostname}:{_container.GetMappedPublicPort(8025)}";
}

[CollectionDefinition("Mailpit")]
public sealed class MailpitCollection : ICollectionFixture<MailpitFixture>;
```

- [ ] **Step 7: Write the failing `SmtpEmailSender` test**

```csharp
namespace SnagList.Infrastructure.Tests.Email;

using System.Net.Http.Json;
using SnagList.Infrastructure.Email;
using SnagList.Infrastructure.Tests.Testing;
using Xunit;

[Collection("Mailpit")]
public class SmtpEmailSenderTests(MailpitFixture fixture)
{
    private sealed record MailpitMessageSummary(string Subject);
    private sealed record MailpitMessagesResponse(List<MailpitMessageSummary> Messages);

    [Fact]
    public async Task SendAsync_delivers_a_message_Mailpit_receives()
    {
        var sender = new SmtpEmailSender(new SmtpEmailSenderOptions
        {
            Host = fixture.Hostname,
            Port = fixture.SmtpPort,
            FromAddress = "snaglist@example.com",
            UseTls = false,
        });

        await sender.SendAsync(
            "maintenance@example.com", "New Snag reported", "A Snag was reported at Head Office.", default);

        using var httpClient = new HttpClient { BaseAddress = new Uri(fixture.HttpBaseUrl) };
        MailpitMessagesResponse? result = null;
        for (var attempt = 0; attempt < 20 && (result is null || result.Messages.Count == 0); attempt++)
        {
            await Task.Delay(250);
            result = await httpClient.GetFromJsonAsync<MailpitMessagesResponse>("/api/v1/messages");
        }

        var message = Assert.Single(result!.Messages);
        Assert.Equal("New Snag reported", message.Subject);
    }
}
```

- [ ] **Step 8: Run test to verify it fails**

Run: `dotnet test tests/SnagList.Infrastructure.Tests --filter SmtpEmailSenderTests`
Expected: FAIL — `SmtpEmailSender`/`SmtpEmailSenderOptions` don't exist.

- [ ] **Step 9: Implement `SmtpEmailSender`**

```csharp
namespace SnagList.Infrastructure.Email;

public sealed class SmtpEmailSenderOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string FromAddress { get; set; } = "";
    public bool UseTls { get; set; } = true;
}
```

```csharp
namespace SnagList.Infrastructure.Email;

using MailKit.Net.Smtp;
using MimeKit;
using SnagList.Application.Abstractions;

public sealed class SmtpEmailSender(SmtpEmailSenderOptions options) : IEmailSender
{
    public async Task SendAsync(string toAddress, string subject, string body, CancellationToken ct)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(options.FromAddress));
        message.To.Add(MailboxAddress.Parse(toAddress));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient();
        await client.ConnectAsync(options.Host, options.Port, options.UseTls, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }
}
```

- [ ] **Step 10: Run test to verify it passes**

Run: `dotnet test tests/SnagList.Infrastructure.Tests --filter SmtpEmailSenderTests`
Expected: PASS.

- [ ] **Step 11: Run the full Infrastructure suite, then commit**

Run: `dotnet test tests/SnagList.Infrastructure.Tests`
Expected: PASS — Infrastructure (Tasks 11–14) is fully green. This is Infrastructure's exit gate
before the API layer (Task 15+) starts wiring these implementations together.

```bash
git add src/SnagList.Infrastructure/Clock src/SnagList.Infrastructure/Audit src/SnagList.Infrastructure/Email \
  tests/SnagList.Infrastructure.Tests/Audit tests/SnagList.Infrastructure.Tests/Email \
  tests/SnagList.Infrastructure.Tests/Testing/MailpitFixture.cs
git commit -m "feat(infrastructure): add SystemClock, EfAuditWriter, SmtpEmailSender"
```

---

## Task 15: Api — Keycloak JWT bearer wiring and role-based authorization policies

`ClaimsPrincipalExtensions` and the two role predicates are pure functions, unit-testable without a
live Keycloak. The `AddJwtBearer` wiring itself (token-signature/issuer validation against a real
Keycloak JWKS endpoint) is an integration concern with nothing to assert until Task 22's local
docker-compose stack is actually running — it's exercised there, not unit-tested here.

**Files:**
- Create: `src/SnagList.Api.Auth.Local/KeycloakAuthenticationExtensions.cs`
- Create: `src/SnagList.Api/Authorization/PolicyNames.cs`
- Create: `src/SnagList.Api/Authorization/SnagListClaimTypes.cs`
- Create: `src/SnagList.Api/Authorization/AuthorizationPolicies.cs`
- Create: `src/SnagList.Api/Authorization/ClaimsPrincipalExtensions.cs`
- Test: `tests/SnagList.Api.Tests/Authorization/AuthorizationPoliciesTests.cs`
- Test: `tests/SnagList.Api.Tests/Authorization/ClaimsPrincipalExtensionsTests.cs`

**Interfaces:**
- Consumes: `StaffRole` (Task 4).
- Produces: `principal.GetStaffId()`, `.GetStaffName()`, `.GetStaffEmail()`,
  `.GetStaffRoles() -> IReadOnlyList<StaffRole>`; `AuthorizationPolicies.IsStaff(roles)`,
  `.IsMaintenance(roles)`; `PolicyNames.Staff`, `.Maintenance` — all consumed by every endpoint
  task from here on (Tasks 17–20) and wired into `Program.cs` in Task 16.

- [ ] **Step 1: Write the failing tests**

```csharp
namespace SnagList.Api.Tests.Authorization;

using SnagList.Api.Authorization;
using SnagList.Domain.Staff;
using Xunit;

public class AuthorizationPoliciesTests
{
    [Fact]
    public void IsStaff_is_true_for_either_role()
    {
        Assert.True(AuthorizationPolicies.IsStaff([StaffRole.Staff]));
        Assert.True(AuthorizationPolicies.IsStaff([StaffRole.Maintenance]));
    }

    [Fact]
    public void IsStaff_is_false_for_no_recognised_roles()
    {
        Assert.False(AuthorizationPolicies.IsStaff([]));
    }

    [Fact]
    public void IsMaintenance_requires_the_Maintenance_role_specifically()
    {
        Assert.False(AuthorizationPolicies.IsMaintenance([StaffRole.Staff]));
        Assert.True(AuthorizationPolicies.IsMaintenance([StaffRole.Maintenance]));
    }
}
```

```csharp
namespace SnagList.Api.Tests.Authorization;

using System.Security.Claims;
using SnagList.Api.Authorization;
using SnagList.Domain.Staff;
using Xunit;

public class ClaimsPrincipalExtensionsTests
{
    private static ClaimsPrincipal PrincipalWith(params Claim[] claims) => new(new ClaimsIdentity(claims, "test"));

    [Fact]
    public void GetStaffId_reads_the_staff_id_claim()
    {
        var principal = PrincipalWith(new Claim(SnagListClaimTypes.StaffId, "U123456"));
        Assert.Equal("U123456", principal.GetStaffId());
    }

    [Fact]
    public void GetStaffId_throws_when_the_claim_is_missing()
    {
        Assert.Throws<InvalidOperationException>(() => PrincipalWith().GetStaffId());
    }

    [Fact]
    public void GetStaffRoles_parses_recognised_values_and_ignores_the_rest()
    {
        var principal = PrincipalWith(
            new Claim(SnagListClaimTypes.Role, "Staff"),
            new Claim(SnagListClaimTypes.Role, "Maintenance"),
            new Claim(SnagListClaimTypes.Role, "SomeUnrelatedAppRole"));

        var roles = principal.GetStaffRoles();

        Assert.Contains(StaffRole.Staff, roles);
        Assert.Contains(StaffRole.Maintenance, roles);
        Assert.Equal(2, roles.Count);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/SnagList.Api.Tests --filter "AuthorizationPoliciesTests|ClaimsPrincipalExtensionsTests"`
Expected: FAIL — the types don't exist yet.

- [ ] **Step 3: Implement**

```csharp
namespace SnagList.Api.Authorization;

public static class PolicyNames
{
    public const string Staff = "Staff";
    public const string Maintenance = "Maintenance";
}
```

```csharp
namespace SnagList.Api.Authorization;

public static class SnagListClaimTypes
{
    public const string StaffId = "staff_id";
    public const string Role = "roles";
}
```

```csharp
namespace SnagList.Api.Authorization;

using SnagList.Domain.Staff;

public static class AuthorizationPolicies
{
    public static bool IsStaff(IReadOnlyList<StaffRole> roles) => roles.Count > 0;
    public static bool IsMaintenance(IReadOnlyList<StaffRole> roles) => roles.Contains(StaffRole.Maintenance);
}
```

```csharp
namespace SnagList.Api.Authorization;

using System.Security.Claims;
using SnagList.Domain.Staff;

public static class ClaimsPrincipalExtensions
{
    public static string GetStaffId(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(SnagListClaimTypes.StaffId)
            ?? throw new InvalidOperationException("Token has no staff_id claim.");

    public static string GetStaffName(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.Name) ?? principal.FindFirstValue("name") ?? "";

    public static string GetStaffEmail(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.Email) ?? principal.FindFirstValue("email") ?? "";

    public static IReadOnlyList<StaffRole> GetStaffRoles(this ClaimsPrincipal principal) =>
        principal.FindAll(SnagListClaimTypes.Role)
            .Select(c => Enum.TryParse<StaffRole>(c.Value, ignoreCase: true, out var role) ? role : (StaffRole?)null)
            .Where(r => r is not null)
            .Select(r => r!.Value)
            .Distinct()
            .ToList();
}
```

```csharp
namespace SnagList.Api.Auth.Local;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public static class KeycloakAuthenticationExtensions
{
    public static IServiceCollection AddKeycloakAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var authority = configuration["Auth:Local:Authority"]
            ?? throw new InvalidOperationException("Auth:Local:Authority is required.");
        var audience = configuration["Auth:Local:Audience"]
            ?? throw new InvalidOperationException("Auth:Local:Audience is required.");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = authority;
                options.Audience = audience;
                // Local/home-lab Keycloak is fronted over plain HTTP inside the compose network;
                // production (Entra ID, Task in the AWS deployment plan) always uses HTTPS.
                options.RequireHttpsMetadata = false;
            });

        return services;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/SnagList.Api.Tests --filter "AuthorizationPoliciesTests|ClaimsPrincipalExtensionsTests"`
Expected: PASS (3 + 3 tests).

- [ ] **Step 5: Add the JWT bearer package and commit**

```bash
dotnet add src/SnagList.Api.Auth.Local package Microsoft.AspNetCore.Authentication.JwtBearer

git add src/SnagList.Api.Auth.Local src/SnagList.Api/Authorization tests/SnagList.Api.Tests/Authorization
git commit -m "feat(api): add Keycloak JWT bearer wiring and role-based authorization policies"
```

---

## Task 16: Api — hypermedia links, problem+json errors, cursor pagination envelope, and `Program.cs`

The composition root. This is the task where every Application/Infrastructure port from Tasks
5–15 gets wired to its implementation, and where the state-dependent hypermedia logic that makes
HATEOAS load-bearing (not decoration — see 03-api-design.md) actually lives.

**Files:**
- Create: `src/SnagList.Api/Contracts/ApiLink.cs`
- Create: `src/SnagList.Api/Contracts/HypermediaResource.cs`
- Create: `src/SnagList.Api/Contracts/PagedResponse.cs`
- Create: `src/SnagList.Api/Hypermedia/SnagLinksBuilder.cs`
- Create: `src/SnagList.Api/ErrorHandling/ProblemDetailsExceptionHandler.cs`
- Create: `src/SnagList.Api/Program.cs`
- Test: `tests/SnagList.Api.Tests/Hypermedia/SnagLinksBuilderTests.cs`
- Test: `tests/SnagList.Api.Tests/ErrorHandling/ProblemDetailsExceptionHandlerTests.cs`
- Test: `tests/SnagList.Api.Tests/HealthEndpointTests.cs`

**Interfaces:**
- Consumes: every port from Task 5 and every implementation from Tasks 11–15.
- Produces: `ApiLink(Href, Method, OperationId)`; `HypermediaResource.Links`;
  `PagedResponse<T>(Items, NextCursor)`; `SnagLinksBuilder.Build(snagId, status,
  reportedByStaffId, callerStaffId, callerRoles) -> IReadOnlyDictionary<string, ApiLink>` —
  consumed by the `Snag` endpoints in Tasks 18–19. A runnable `Program.cs` — every later Api task
  (17–20) adds its own `app.MapXEndpoints();` line to it plus its own endpoint file, rather than
  this task guessing at endpoints it doesn't implement yet.

- [ ] **Step 1: Write `ApiLink`, `HypermediaResource`, `PagedResponse`**

```csharp
namespace SnagList.Api.Contracts;

public sealed record ApiLink(string Href, string Method, string OperationId);
```

```csharp
namespace SnagList.Api.Contracts;

using System.Text.Json.Serialization;

public abstract class HypermediaResource
{
    [JsonPropertyName("_links")]
    public required IReadOnlyDictionary<string, ApiLink> Links { get; init; }
}
```

```csharp
namespace SnagList.Api.Contracts;

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, string? NextCursor);
```

- [ ] **Step 2: Write the failing `SnagLinksBuilder` tests**

```csharp
namespace SnagList.Api.Tests.Hypermedia;

using SnagList.Api.Hypermedia;
using SnagList.Domain.Snags;
using SnagList.Domain.Staff;
using Xunit;

public class SnagLinksBuilderTests
{
    private static readonly Guid SnagId = Guid.NewGuid();

    [Fact]
    public void Reporter_sees_edit_and_withdraw_while_Reported()
    {
        var links = SnagLinksBuilder.Build(SnagId, SnagStatus.Reported, "U1", "U1", [StaffRole.Staff]);

        Assert.Contains("edit", links.Keys);
        Assert.Contains("withdraw", links.Keys);
        Assert.DoesNotContain("acknowledge", links.Keys);
    }

    [Fact]
    public void A_different_staff_member_does_not_see_edit_or_withdraw()
    {
        var links = SnagLinksBuilder.Build(SnagId, SnagStatus.Reported, "U1", "U2", [StaffRole.Staff]);

        Assert.DoesNotContain("edit", links.Keys);
        Assert.DoesNotContain("withdraw", links.Keys);
    }

    [Fact]
    public void Maintenance_sees_acknowledge_and_reject_while_Reported()
    {
        var links = SnagLinksBuilder.Build(SnagId, SnagStatus.Reported, "U1", "U9", [StaffRole.Maintenance]);

        Assert.Contains("acknowledge", links.Keys);
        Assert.Contains("reject", links.Keys);
        Assert.DoesNotContain("edit", links.Keys);
    }

    [Fact]
    public void Maintenance_sees_only_close_while_Resolved()
    {
        var links = SnagLinksBuilder.Build(SnagId, SnagStatus.Resolved, "U1", "U9", [StaffRole.Maintenance]);

        Assert.Contains("close", links.Keys);
        Assert.DoesNotContain("acknowledge", links.Keys);
        Assert.DoesNotContain("resolve", links.Keys);
    }

    [Fact]
    public void Comments_and_photos_links_are_always_present_regardless_of_status_or_role()
    {
        var links = SnagLinksBuilder.Build(SnagId, SnagStatus.Closed, "U1", "U9", [StaffRole.Staff]);

        Assert.Contains("comments", links.Keys);
        Assert.Contains("photos", links.Keys);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/SnagList.Api.Tests --filter SnagLinksBuilderTests`
Expected: FAIL — `SnagLinksBuilder` does not exist.

- [ ] **Step 4: Implement `SnagLinksBuilder`**

```csharp
namespace SnagList.Api.Hypermedia;

using SnagList.Api.Authorization;
using SnagList.Api.Contracts;
using SnagList.Domain.Snags;
using SnagList.Domain.Staff;

public static class SnagLinksBuilder
{
    public static IReadOnlyDictionary<string, ApiLink> Build(
        Guid snagId, SnagStatus status, string reportedByStaffId,
        string callerStaffId, IReadOnlyList<StaffRole> callerRoles)
    {
        var links = new Dictionary<string, ApiLink>
        {
            ["self"] = new($"/api/v1/snags/{snagId}", "GET", "GetSnag"),
        };

        var isReporter = reportedByStaffId == callerStaffId;
        var isMaintenance = AuthorizationPolicies.IsMaintenance(callerRoles);

        if (status == SnagStatus.Reported && isReporter)
        {
            links["edit"] = new($"/api/v1/snags/{snagId}", "PATCH", "EditSnag");
            links["withdraw"] = new($"/api/v1/snags/{snagId}/withdraw", "POST", "WithdrawSnag");
        }

        if (isMaintenance)
        {
            switch (status)
            {
                case SnagStatus.Reported:
                    links["acknowledge"] = new($"/api/v1/snags/{snagId}/acknowledge", "POST", "AcknowledgeSnag");
                    links["reject"] = new($"/api/v1/snags/{snagId}/reject", "POST", "RejectSnag");
                    break;
                case SnagStatus.Acknowledged:
                    links["start"] = new($"/api/v1/snags/{snagId}/start", "POST", "StartSnagWork");
                    links["reject"] = new($"/api/v1/snags/{snagId}/reject", "POST", "RejectSnag");
                    break;
                case SnagStatus.InProgress:
                    links["resolve"] = new($"/api/v1/snags/{snagId}/resolve", "POST", "ResolveSnag");
                    break;
                case SnagStatus.Resolved:
                    links["close"] = new($"/api/v1/snags/{snagId}/close", "POST", "CloseSnag");
                    break;
            }
        }

        links["comments"] = new($"/api/v1/snags/{snagId}/comments", "POST", "AddSnagComment");
        links["photos"] = new($"/api/v1/snags/{snagId}/photos", "POST", "UploadSnagPhoto");

        return links;
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/SnagList.Api.Tests --filter SnagLinksBuilderTests`
Expected: PASS (5 tests).

- [ ] **Step 6: Write the failing `ProblemDetailsExceptionHandler` tests**

```csharp
namespace SnagList.Api.Tests.ErrorHandling;

using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SnagList.Api.ErrorHandling;
using SnagList.Application.Snags;
using Xunit;

public class ProblemDetailsExceptionHandlerTests
{
    private static async Task<(bool handled, int statusCode, ProblemDetails? body)> Handle(Exception exception)
    {
        var handler = new ProblemDetailsExceptionHandler();
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };

        var handled = await handler.TryHandleAsync(context, exception, default);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = handled ? await JsonSerializer.DeserializeAsync<ProblemDetails>(context.Response.Body) : null;
        return (handled, context.Response.StatusCode, body);
    }

    [Fact]
    public async Task Maps_SnagVersionConflictException_to_409_with_a_stable_type_uri()
    {
        var (handled, statusCode, body) = await Handle(new SnagVersionConflictException(Guid.NewGuid(), 2, 3));

        Assert.True(handled);
        Assert.Equal(409, statusCode);
        Assert.Equal("https://snaglist.example/errors/version-conflict", body!.Type);
    }

    [Fact]
    public async Task Maps_UnauthorizedSnagActionException_to_403()
    {
        var (handled, statusCode, _) = await Handle(new UnauthorizedSnagActionException("edit"));

        Assert.True(handled);
        Assert.Equal(403, statusCode);
    }

    [Fact]
    public async Task Maps_SnagNotFoundException_to_404()
    {
        var (handled, statusCode, _) = await Handle(new SnagNotFoundException(Guid.NewGuid()));

        Assert.True(handled);
        Assert.Equal(404, statusCode);
    }

    [Fact]
    public async Task Returns_false_for_an_unmapped_exception_so_the_default_handler_takes_over()
    {
        var (handled, _, _) = await Handle(new InvalidOperationException("unexpected"));

        Assert.False(handled);
    }
}
```

- [ ] **Step 7: Run test to verify it fails**

Run: `dotnet test tests/SnagList.Api.Tests --filter ProblemDetailsExceptionHandlerTests`
Expected: FAIL — `ProblemDetailsExceptionHandler` does not exist.

- [ ] **Step 8: Implement `ProblemDetailsExceptionHandler`**

```csharp
namespace SnagList.Api.ErrorHandling;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SnagList.Application.Locations;
using SnagList.Application.Snags;
using SnagList.Domain.Snags;

public sealed class ProblemDetailsExceptionHandler : IExceptionHandler
{
    private const string BaseUri = "https://snaglist.example/errors";

    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, type, title) = exception switch
        {
            SnagNotFoundException or LocationNotFoundException =>
                (StatusCodes.Status404NotFound, $"{BaseUri}/not-found", "Not found"),
            SnagVersionConflictException =>
                (StatusCodes.Status409Conflict, $"{BaseUri}/version-conflict", "Version conflict"),
            UnauthorizedSnagActionException =>
                (StatusCodes.Status403Forbidden, $"{BaseUri}/not-authorized", "Not authorized"),
            InvalidSnagStatusTransitionException =>
                (StatusCodes.Status409Conflict, $"{BaseUri}/invalid-status-transition", "Invalid status transition"),
            SnagNotEditableException =>
                (StatusCodes.Status409Conflict, $"{BaseUri}/not-editable", "Snag is not editable"),
            SnagPhotoLimitExceededException =>
                (StatusCodes.Status422UnprocessableEntity, $"{BaseUri}/photo-limit-exceeded", "Photo limit exceeded"),
            LocationNotActiveException =>
                (StatusCodes.Status422UnprocessableEntity, $"{BaseUri}/location-not-active", "Location is not active"),
            _ => (0, "", ""),
        };

        if (status == 0) return false;

        var problemDetails = new ProblemDetails
        {
            Status = status,
            Type = type,
            Title = title,
            Detail = exception.Message,
        };
        if (exception is SnagVersionConflictException conflict)
        {
            problemDetails.Extensions["expectedVersion"] = conflict.ExpectedVersion;
            problemDetails.Extensions["actualVersion"] = conflict.ActualVersion;
        }

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problemDetails, ct);
        return true;
    }
}
```

- [ ] **Step 9: Run test to verify it passes**

Run: `dotnet test tests/SnagList.Api.Tests --filter ProblemDetailsExceptionHandlerTests`
Expected: PASS (4 tests).

- [ ] **Step 10: Write `Program.cs`**

```csharp
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SnagList.Api.Auth.Local;
using SnagList.Api.Authorization;
using SnagList.Api.ErrorHandling;
using SnagList.Application.Abstractions;
using SnagList.Infrastructure.Audit;
using SnagList.Infrastructure.Clock;
using SnagList.Infrastructure.Email;
using SnagList.Infrastructure.Persistence;
using SnagList.Infrastructure.Persistence.Queries;
using SnagList.Infrastructure.Persistence.Repositories;
using SnagList.Infrastructure.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<SnagListDbContext>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("SnagList")
        ?? throw new InvalidOperationException("Connection string 'SnagList' is required.")));

builder.Services.AddScoped<ILocationRepository, EfLocationRepository>();
builder.Services.AddScoped<ISnagRepository, EfSnagRepository>();
builder.Services.AddScoped<IUnitOfWork, EfUnitOfWork>();
builder.Services.AddScoped<ILocationQueries, EfLocationQueries>();
builder.Services.AddScoped<ISnagQueries, EfSnagQueries>();
builder.Services.AddScoped<IAuditWriter, EfAuditWriter>();
builder.Services.AddSingleton<IClock, SystemClock>();

var storage = builder.Configuration.GetSection("Storage");
var storageBucket = storage["BucketName"] ?? throw new InvalidOperationException("Storage:BucketName is required.");
builder.Services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(
    new BasicAWSCredentials(
        storage["AccessKey"] ?? throw new InvalidOperationException("Storage:AccessKey is required."),
        storage["SecretKey"] ?? throw new InvalidOperationException("Storage:SecretKey is required.")),
    new AmazonS3Config
    {
        ServiceURL = storage["ServiceUrl"] ?? throw new InvalidOperationException("Storage:ServiceUrl is required."),
        ForcePathStyle = true,
    }));
builder.Services.AddSingleton<IBlobStorage>(sp => new S3CompatibleBlobStorage(sp.GetRequiredService<IAmazonS3>(), storageBucket));

builder.Services.Configure<SmtpEmailSenderOptions>(builder.Configuration.GetSection("Email"));
builder.Services.AddSingleton<IEmailSender>(sp =>
    new SmtpEmailSender(sp.GetRequiredService<IOptions<SmtpEmailSenderOptions>>().Value));

builder.Services.AddKeycloakAuthentication(builder.Configuration);
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(PolicyNames.Staff, policy => policy.RequireAssertion(ctx => AuthorizationPolicies.IsStaff(ctx.User.GetStaffRoles())))
    .AddPolicy(PolicyNames.Maintenance, policy => policy.RequireAssertion(ctx => AuthorizationPolicies.IsMaintenance(ctx.User.GetStaffRoles())));

builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapOpenApi("/openapi/v1.json");

app.Run();

public partial class Program; // exposes the entry point for WebApplicationFactory<Program> in tests
```

- [ ] **Step 11: Write the failing host smoke test**

```csharp
namespace SnagList.Api.Tests;

using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

public class HealthEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Health_endpoint_responds_ok_without_authentication()
    {
        var client = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:SnagList", "Host=localhost;Database=snaglist_test;Username=postgres;Password=postgres");
            builder.UseSetting("Storage:BucketName", "test-bucket");
            builder.UseSetting("Storage:AccessKey", "test");
            builder.UseSetting("Storage:SecretKey", "test");
            builder.UseSetting("Storage:ServiceUrl", "http://localhost:9000");
            builder.UseSetting("Email:Host", "localhost");
            builder.UseSetting("Email:FromAddress", "snaglist@example.com");
            builder.UseSetting("Auth:Local:Authority", "http://localhost:8080/realms/snaglist");
            builder.UseSetting("Auth:Local:Audience", "snaglist-api");
        }).CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

Note: this test only needs the DI graph to *construct* successfully — none of these config values
need to point at anything real, since nothing here connects eagerly at startup (see Task 11–15).

- [ ] **Step 12: Run test to verify it fails, then passes**

Run: `dotnet test tests/SnagList.Api.Tests --filter HealthEndpointTests`
Expected: first FAIL (`Program` isn't a runnable host with `/health` yet — it doesn't exist before
this step), then, once Step 10 lands, PASS.

Run: `dotnet test tests/SnagList.Api.Tests`
Expected: PASS — the full Api test suite (Tasks 15–16) is green.

- [ ] **Step 13: Add remaining packages and commit**

```bash
dotnet add src/SnagList.Api package Microsoft.AspNetCore.OpenApi
dotnet add tests/SnagList.Api.Tests package Microsoft.AspNetCore.Mvc.Testing

git add src/SnagList.Api tests/SnagList.Api.Tests
git commit -m "feat(api): add hypermedia links, problem+json errors, and the composition root"
```

---

## Task 17: Api — `Location` endpoints, and the shared integration-test harness

Introduces `SnagListApiFactory`, the shared `WebApplicationFactory<Program>` every remaining Api
integration test (Tasks 18–20) reuses: real Postgres via Testcontainers (this is what's actually
under test — CRUD, authorization, hypermedia, pagination), with `IBlobStorage`/`IEmailSender`
swapped for in-project fakes (their real implementations already have dedicated Testcontainers
coverage in Tasks 13–14; re-proving that wiring here would be redundant), and a `TestAuthHandler`
standing in for Keycloak so tests can assert real authorization behavior without a live IdP.

**Files:**
- Create: `tests/SnagList.Api.Tests/Testing/TestAuthHandler.cs`
- Create: `tests/SnagList.Api.Tests/Testing/FakeBlobStorage.cs`
- Create: `tests/SnagList.Api.Tests/Testing/FakeEmailSender.cs`
- Create: `tests/SnagList.Api.Tests/Testing/SnagListApiFactory.cs`
- Create: `src/SnagList.Api/Contracts/Locations/LocationResponse.cs`
- Create: `src/SnagList.Api/Contracts/Locations/LocationRequests.cs`
- Create: `src/SnagList.Api/Hypermedia/LocationLinksBuilder.cs`
- Create: `src/SnagList.Api/Endpoints/LocationEndpoints.cs`
- Modify: `src/SnagList.Api/Program.cs` — register the `Location` command/query handlers, call
  `app.MapLocationEndpoints();`
- Test: `tests/SnagList.Api.Tests/Endpoints/LocationEndpointsTests.cs`

**Interfaces:**
- Consumes: `CreateLocationCommandHandler`, `UpdateLocationCommandHandler`,
  `RetireLocationCommandHandler`, `ListLocationsQueryHandler` (Task 5); `ILocationRepository`
  (Task 5, used directly for the single-`Location` GET, which has no separate query DTO since
  `Location` has no nested collections worth a dedicated read model).
- Produces: `SnagListApiFactory.CreateAuthenticatedClient(staffId, roles...) -> HttpClient` —
  reused by every later Api integration test task.

- [ ] **Step 1: Write the shared test-authentication and fake-adapter infrastructure**

```csharp
namespace SnagList.Api.Tests.Testing;

using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SnagList.Api.Authorization;

public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-StaffId", out var staffId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new(SnagListClaimTypes.StaffId, staffId!),
            new(ClaimTypes.Name, Request.Headers["X-Test-Name"] is { Count: > 0 } n ? n.ToString() : "Test User"),
        };
        if (Request.Headers.TryGetValue("X-Test-Roles", out var roles))
        {
            claims.AddRange(roles.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(r => new Claim(SnagListClaimTypes.Role, r)));
        }

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
```

```csharp
namespace SnagList.Api.Tests.Testing;

using SnagList.Application.Abstractions;

public sealed class FakeBlobStorage : IBlobStorage
{
    public Task PutAsync(string key, Stream content, string contentType, CancellationToken ct) => Task.CompletedTask;

    public Task<Stream> GetAsync(string key, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream());

    public Task<Uri> GetPresignedGetUrlAsync(string key, TimeSpan expiry, CancellationToken ct) =>
        Task.FromResult(new Uri($"https://fake-storage.test/{key}"));

    public Task DeleteAsync(string key, CancellationToken ct) => Task.CompletedTask;
}
```

```csharp
namespace SnagList.Api.Tests.Testing;

using SnagList.Application.Abstractions;

public sealed class FakeEmailSender : IEmailSender
{
    public readonly List<(string To, string Subject)> SentEmails = [];

    public Task SendAsync(string toAddress, string subject, string body, CancellationToken ct)
    {
        SentEmails.Add((toAddress, subject));
        return Task.CompletedTask;
    }
}
```

```csharp
namespace SnagList.Api.Tests.Testing;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SnagList.Application.Abstractions;
using SnagList.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

public sealed class SnagListApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<SnagListDbContext>().Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Storage:BucketName", "test-bucket");
        builder.UseSetting("Storage:AccessKey", "test");
        builder.UseSetting("Storage:SecretKey", "test");
        builder.UseSetting("Storage:ServiceUrl", "http://localhost:9000");
        builder.UseSetting("Email:Host", "localhost");
        builder.UseSetting("Email:FromAddress", "snaglist@example.com");
        builder.UseSetting("Auth:Local:Authority", "http://localhost:8080/realms/snaglist");
        builder.UseSetting("Auth:Local:Audience", "snaglist-api");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<SnagListDbContext>>();
            services.AddDbContext<SnagListDbContext>(o => o.UseNpgsql(_postgres.GetConnectionString()));

            services.RemoveAll<IBlobStorage>();
            services.AddSingleton<IBlobStorage, FakeBlobStorage>();
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender, FakeEmailSender>();

            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }

    public HttpClient CreateAuthenticatedClient(string staffId, params string[] roles)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-StaffId", staffId);
        client.DefaultRequestHeaders.Add("X-Test-Roles", string.Join(',', roles));
        return client;
    }
}
```

- [ ] **Step 2: Write the failing `Location` endpoint contracts and hypermedia builder**

```csharp
namespace SnagList.Api.Contracts.Locations;

public sealed class LocationResponse : HypermediaResource
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Address { get; init; }
    public required bool IsActive { get; init; }
}
```

```csharp
namespace SnagList.Api.Contracts.Locations;

public sealed record CreateLocationRequest(string Name, string Address);
public sealed record UpdateLocationRequest(string Name, string Address);
```

```csharp
namespace SnagList.Api.Hypermedia;

using SnagList.Api.Authorization;
using SnagList.Api.Contracts;
using SnagList.Domain.Staff;

public static class LocationLinksBuilder
{
    public static IReadOnlyDictionary<string, ApiLink> Build(Guid locationId, bool isActive, IReadOnlyList<StaffRole> callerRoles)
    {
        var links = new Dictionary<string, ApiLink>
        {
            ["self"] = new($"/api/v1/locations/{locationId}", "GET", "GetLocation"),
        };

        if (AuthorizationPolicies.IsMaintenance(callerRoles))
        {
            links["update"] = new($"/api/v1/locations/{locationId}", "PUT", "UpdateLocation");
            if (isActive)
            {
                links["retire"] = new($"/api/v1/locations/{locationId}/retire", "POST", "RetireLocation");
            }
        }

        return links;
    }
}
```

- [ ] **Step 3: Write the failing endpoint tests**

```csharp
namespace SnagList.Api.Tests.Endpoints;

using System.Net;
using System.Net.Http.Json;
using SnagList.Api.Contracts;
using SnagList.Api.Contracts.Locations;
using SnagList.Api.Tests.Testing;
using Xunit;

public class LocationEndpointsTests(SnagListApiFactory factory) : IClassFixture<SnagListApiFactory>
{
    [Fact]
    public async Task Maintenance_can_create_then_Staff_can_list_and_see_it()
    {
        var maintenanceClient = factory.CreateAuthenticatedClient("U900001", "Maintenance");
        var createResponse = await maintenanceClient.PostAsJsonAsync(
            "/api/v1/locations", new CreateLocationRequest("Northern Office", "42 North Rd"));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var staffClient = factory.CreateAuthenticatedClient("U100001", "Staff");
        var list = await staffClient.GetFromJsonAsync<PagedResponse<LocationResponse>>("/api/v1/locations?limit=50");

        Assert.Contains(list!.Items, l => l.Name == "Northern Office");
    }

    [Fact]
    public async Task Staff_cannot_create_a_Location()
    {
        var staffClient = factory.CreateAuthenticatedClient("U100002", "Staff");

        var response = await staffClient.PostAsJsonAsync(
            "/api/v1/locations", new CreateLocationRequest("Unauthorized Site", "1 Nowhere"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_unauthenticated_request_is_rejected()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/locations");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Staff_only_sees_the_retire_link_when_the_Location_is_still_active_and_they_are_Maintenance()
    {
        var maintenanceClient = factory.CreateAuthenticatedClient("U900002", "Maintenance");
        var createResponse = await maintenanceClient.PostAsJsonAsync(
            "/api/v1/locations", new CreateLocationRequest("Engineering Site", "3 Park Rd"));
        var created = await createResponse.Content.ReadFromJsonAsync<CreatedLocation>();

        var asMaintenance = await maintenanceClient.GetFromJsonAsync<LocationResponse>($"/api/v1/locations/{created!.Id}");
        Assert.Contains("retire", asMaintenance!.Links.Keys);

        var staffClient = factory.CreateAuthenticatedClient("U100003", "Staff");
        var asStaff = await staffClient.GetFromJsonAsync<LocationResponse>($"/api/v1/locations/{created.Id}");
        Assert.DoesNotContain("retire", asStaff!.Links.Keys);
    }

    private sealed record CreatedLocation(Guid Id);
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test tests/SnagList.Api.Tests --filter LocationEndpointsTests`
Expected: FAIL — `LocationEndpoints`/`MapLocationEndpoints` don't exist yet.

- [ ] **Step 5: Implement the endpoints**

```csharp
namespace SnagList.Api.Endpoints;

using Microsoft.AspNetCore.Mvc;
using SnagList.Api.Authorization;
using SnagList.Api.Contracts;
using SnagList.Api.Contracts.Locations;
using SnagList.Api.Hypermedia;
using SnagList.Application.Abstractions;
using SnagList.Application.Locations.Commands;
using SnagList.Application.Locations.Queries;

public static class LocationEndpoints
{
    public static IEndpointRouteBuilder MapLocationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/locations").RequireAuthorization(PolicyNames.Staff);

        group.MapGet("", async (
            [FromQuery] bool includeRetired, [FromQuery] string? cursor, [FromQuery] int limit,
            ListLocationsQueryHandler handler, HttpContext http, CancellationToken ct) =>
        {
            var page = await handler.HandleAsync(
                new ListLocationsQuery(includeRetired, cursor, limit is > 0 and <= 100 ? limit : 20), ct);
            var roles = http.User.GetStaffRoles();
            var items = page.Items.Select(l => new LocationResponse
            {
                Id = l.Id,
                Name = l.Name,
                Address = l.Address,
                IsActive = l.IsActive,
                Links = LocationLinksBuilder.Build(l.Id, l.IsActive, roles),
            }).ToList();
            return Results.Ok(new PagedResponse<LocationResponse>(items, page.NextCursor));
        }).WithName("ListLocations");

        group.MapGet("/{id:guid}", async (Guid id, ILocationRepository repository, HttpContext http, CancellationToken ct) =>
        {
            var location = await repository.GetAsync(id, ct);
            if (location is null) return Results.NotFound();

            var roles = http.User.GetStaffRoles();
            return Results.Ok(new LocationResponse
            {
                Id = location.Id,
                Name = location.Name,
                Address = location.Address,
                IsActive = location.IsActive,
                Links = LocationLinksBuilder.Build(location.Id, location.IsActive, roles),
            });
        }).WithName("GetLocation");

        group.MapPost("", async (CreateLocationRequest request, CreateLocationCommandHandler handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(new CreateLocationCommand(request.Name, request.Address), ct);
            return Results.Created($"/api/v1/locations/{id}", new { id });
        }).RequireAuthorization(PolicyNames.Maintenance).WithName("CreateLocation");

        group.MapPut("/{id:guid}", async (
            Guid id, UpdateLocationRequest request, UpdateLocationCommandHandler handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new UpdateLocationCommand(id, request.Name, request.Address), ct);
            return Results.NoContent();
        }).RequireAuthorization(PolicyNames.Maintenance).WithName("UpdateLocation");

        group.MapPost("/{id:guid}/retire", async (Guid id, RetireLocationCommandHandler handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new RetireLocationCommand(id), ct);
            return Results.NoContent();
        }).RequireAuthorization(PolicyNames.Maintenance).WithName("RetireLocation");

        return app;
    }
}
```

- [ ] **Step 6: Wire it into `Program.cs`**

Modify `src/SnagList.Api/Program.cs`: add, alongside the existing port registrations —

```csharp
builder.Services.AddScoped<CreateLocationCommandHandler>();
builder.Services.AddScoped<UpdateLocationCommandHandler>();
builder.Services.AddScoped<RetireLocationCommandHandler>();
builder.Services.AddScoped<ListLocationsQueryHandler>();
```

— and, after `app.MapOpenApi(...)` and before `app.Run();`:

```csharp
app.MapLocationEndpoints();
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/SnagList.Api.Tests --filter LocationEndpointsTests`
Expected: PASS (4 tests). (Requires Docker running locally for the Testcontainers Postgres.)

- [ ] **Step 8: Add packages and commit**

```bash
dotnet add tests/SnagList.Api.Tests package Testcontainers.PostgreSql

git add src/SnagList.Api tests/SnagList.Api.Tests
git commit -m "feat(api): add Location endpoints and the shared integration-test harness"
```

---

## Task 18: Api — `Snag` endpoints: report, get, list, edit, withdraw

**Files:**
- Create: `src/SnagList.Api/Contracts/Snags/SnagSummaryResponse.cs`
- Create: `src/SnagList.Api/Contracts/Snags/SnagDetailResponse.cs`
- Create: `src/SnagList.Api/Contracts/Snags/SnagRequests.cs`
- Modify: `src/SnagList.Api/Hypermedia/SnagLinksBuilder.cs` — add `BuildSummaryLinks(snagId)`, a
  lighter link set for list rows (no per-row transition links; a client follows `self` to the
  single-resource GET to discover those, matching 03-api-design.md's list/detail split).
- Create: `src/SnagList.Api/Endpoints/SnagEndpoints.cs`
- Modify: `src/SnagList.Api/Program.cs` — register the `Snag` command/query handlers, call
  `app.MapSnagEndpoints();`
- Test: `tests/SnagList.Api.Tests/Hypermedia/SnagLinksBuilderTests.cs` — one added case
- Test: `tests/SnagList.Api.Tests/Endpoints/SnagEndpointsTests.cs`

**Interfaces:**
- Consumes: `ReportSnagCommandHandler` (Task 6), `EditSnagCommandHandler`,
  `WithdrawSnagCommandHandler` (Task 7), `ListSnagsQueryHandler`, `GetSnagQueryHandler` (Task 10),
  `SnagLinksBuilder.Build` (Task 16).
- Produces: `SnagLinksBuilder.BuildSummaryLinks(snagId)` — reused by the status-transition/comment
  endpoints in Task 19 wherever a summary-shaped response is returned.

- [ ] **Step 1: Add the failing `BuildSummaryLinks` test**

```csharp
// Append to SnagLinksBuilderTests (tests/SnagList.Api.Tests/Hypermedia/SnagLinksBuilderTests.cs)

[Fact]
public void BuildSummaryLinks_carries_only_self_comments_and_photos()
{
    var links = SnagLinksBuilder.BuildSummaryLinks(SnagId);

    Assert.Equal(["self", "comments", "photos"], links.Keys.OrderBy(k => k));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/SnagList.Api.Tests --filter BuildSummaryLinks_carries_only_self_comments_and_photos`
Expected: FAIL — `BuildSummaryLinks` does not exist.

- [ ] **Step 3: Add `BuildSummaryLinks` to `SnagLinksBuilder`**

```csharp
// Add to SnagLinksBuilder (src/SnagList.Api/Hypermedia/SnagLinksBuilder.cs)

public static IReadOnlyDictionary<string, ApiLink> BuildSummaryLinks(Guid snagId) => new Dictionary<string, ApiLink>
{
    ["self"] = new($"/api/v1/snags/{snagId}", "GET", "GetSnag"),
    ["comments"] = new($"/api/v1/snags/{snagId}/comments", "POST", "AddSnagComment"),
    ["photos"] = new($"/api/v1/snags/{snagId}/photos", "POST", "UploadSnagPhoto"),
};
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/SnagList.Api.Tests --filter BuildSummaryLinks_carries_only_self_comments_and_photos`
Expected: PASS.

- [ ] **Step 5: Write the contracts**

```csharp
namespace SnagList.Api.Contracts.Snags;

using SnagList.Api.Contracts;
using SnagList.Domain.Snags;

public sealed class SnagSummaryResponse : HypermediaResource
{
    public required Guid Id { get; init; }
    public required Guid LocationId { get; init; }
    public required string SubLocation { get; init; }
    public required SnagCategory Category { get; init; }
    public required SnagSeverity Severity { get; init; }
    public required SnagStatus Status { get; init; }
    public required string ReportedByName { get; init; }
    public required DateTimeOffset ReportedAt { get; init; }
    public required int Version { get; init; }
}
```

```csharp
namespace SnagList.Api.Contracts.Snags;

using SnagList.Api.Contracts;
using SnagList.Domain.Snags;

public sealed record SnagCommentResponse(Guid Id, string AuthorStaffId, string AuthorName, string Body, DateTimeOffset CreatedAt);

public sealed record SnagPhotoResponse(
    string PhotoKey, string FileName, string ContentType, long SizeBytes, DateTimeOffset UploadedAt, ApiLink Href);

public sealed class SnagDetailResponse : HypermediaResource
{
    public required Guid Id { get; init; }
    public required Guid LocationId { get; init; }
    public required string SubLocation { get; init; }
    public required SnagCategory Category { get; init; }
    public required SnagSeverity Severity { get; init; }
    public required string Description { get; init; }
    public required SnagStatus Status { get; init; }
    public required string ReportedByStaffId { get; init; }
    public required string ReportedByName { get; init; }
    public required DateTimeOffset ReportedAt { get; init; }
    public required int Version { get; init; }
    public required IReadOnlyList<SnagCommentResponse> Comments { get; init; }
    public required IReadOnlyList<SnagPhotoResponse> Photos { get; init; }
}
```

```csharp
namespace SnagList.Api.Contracts.Snags;

using SnagList.Domain.Snags;

public sealed record ReportSnagRequest(
    Guid LocationId, string SubLocation, SnagCategory Category, SnagSeverity Severity, string Description);

public sealed record EditSnagRequest(
    string SubLocation, SnagCategory Category, SnagSeverity Severity, string Description, int ExpectedVersion);

public sealed record WithdrawSnagRequest(int ExpectedVersion);
```

- [ ] **Step 6: Write the failing endpoint tests**

```csharp
namespace SnagList.Api.Tests.Endpoints;

using System.Net;
using System.Net.Http.Json;
using SnagList.Api.Contracts;
using SnagList.Api.Contracts.Locations;
using SnagList.Api.Contracts.Snags;
using SnagList.Api.Tests.Testing;
using SnagList.Domain.Snags;
using Xunit;

public class SnagEndpointsTests(SnagListApiFactory factory) : IClassFixture<SnagListApiFactory>
{
    private async Task<Guid> CreateLocationAsync(string name)
    {
        var client = factory.CreateAuthenticatedClient("U900000", "Maintenance");
        var response = await client.PostAsJsonAsync("/api/v1/locations", new CreateLocationRequest(name, "1 Main St"));
        var created = await response.Content.ReadFromJsonAsync<CreatedResource>();
        return created!.Id;
    }

    [Fact]
    public async Task Reporter_can_report_then_get_their_Snag_with_edit_and_withdraw_links()
    {
        var locationId = await CreateLocationAsync("Head Office A");
        var reporter = factory.CreateAuthenticatedClient("U100010", "Staff");

        var reportResponse = await reporter.PostAsJsonAsync("/api/v1/snags",
            new ReportSnagRequest(locationId, "3rd floor", SnagCategory.Electrical, SnagSeverity.Medium, "Flickering light"));
        Assert.Equal(HttpStatusCode.Created, reportResponse.StatusCode);
        var created = await reportResponse.Content.ReadFromJsonAsync<CreatedResource>();

        var detail = await reporter.GetFromJsonAsync<SnagDetailResponse>($"/api/v1/snags/{created!.Id}");

        Assert.Equal(SnagStatus.Reported, detail!.Status);
        Assert.Contains("edit", detail.Links.Keys);
        Assert.Contains("withdraw", detail.Links.Keys);
    }

    [Fact]
    public async Task List_filters_by_status()
    {
        var locationId = await CreateLocationAsync("Head Office B");
        var reporter = factory.CreateAuthenticatedClient("U100011", "Staff");
        await reporter.PostAsJsonAsync("/api/v1/snags",
            new ReportSnagRequest(locationId, "4th floor", SnagCategory.Plumbing, SnagSeverity.Low, "Dripping tap"));

        var page = await reporter.GetFromJsonAsync<PagedResponse<SnagSummaryResponse>>(
            $"/api/v1/snags?locationId={locationId}&status=Acknowledged&limit=50");

        Assert.Empty(page!.Items);
    }

    [Fact]
    public async Task Reporter_can_edit_while_Reported_but_a_different_staff_member_cannot()
    {
        var locationId = await CreateLocationAsync("Head Office C");
        var reporter = factory.CreateAuthenticatedClient("U100012", "Staff");
        var reportResponse = await reporter.PostAsJsonAsync("/api/v1/snags",
            new ReportSnagRequest(locationId, "5th floor", SnagCategory.Other, SnagSeverity.Low, "Squeaky door"));
        var created = await reportResponse.Content.ReadFromJsonAsync<CreatedResource>();

        var editResponse = await reporter.PatchAsJsonAsync($"/api/v1/snags/{created!.Id}",
            new EditSnagRequest("5th floor, room 5.02", SnagCategory.Other, SnagSeverity.Low, "Squeaky door, worse now", 1));
        Assert.Equal(HttpStatusCode.NoContent, editResponse.StatusCode);

        var otherStaff = factory.CreateAuthenticatedClient("U100099", "Staff");
        var forbidden = await otherStaff.PatchAsJsonAsync($"/api/v1/snags/{created.Id}",
            new EditSnagRequest("tampering", SnagCategory.Other, SnagSeverity.Low, "tampering", 2));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Editing_with_a_stale_expectedVersion_returns_409_with_the_conflict_type()
    {
        var locationId = await CreateLocationAsync("Head Office D");
        var reporter = factory.CreateAuthenticatedClient("U100013", "Staff");
        var reportResponse = await reporter.PostAsJsonAsync("/api/v1/snags",
            new ReportSnagRequest(locationId, "6th floor", SnagCategory.Other, SnagSeverity.Low, "desc"));
        var created = await reportResponse.Content.ReadFromJsonAsync<CreatedResource>();

        var response = await reporter.PatchAsJsonAsync($"/api/v1/snags/{created!.Id}",
            new EditSnagRequest("6th floor", SnagCategory.Other, SnagSeverity.Low, "desc", expectedVersion: 999));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsBody>();
        Assert.Equal("https://snaglist.example/errors/version-conflict", problem!.Type);
    }

    [Fact]
    public async Task Reporter_can_withdraw_while_Reported()
    {
        var locationId = await CreateLocationAsync("Head Office E");
        var reporter = factory.CreateAuthenticatedClient("U100014", "Staff");
        var reportResponse = await reporter.PostAsJsonAsync("/api/v1/snags",
            new ReportSnagRequest(locationId, "7th floor", SnagCategory.Other, SnagSeverity.Low, "desc"));
        var created = await reportResponse.Content.ReadFromJsonAsync<CreatedResource>();

        var response = await reporter.PostAsJsonAsync($"/api/v1/snags/{created!.Id}/withdraw", new WithdrawSnagRequest(1));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var detail = await reporter.GetFromJsonAsync<SnagDetailResponse>($"/api/v1/snags/{created.Id}");
        Assert.Equal(SnagStatus.Withdrawn, detail!.Status);
    }

    private sealed record CreatedResource(Guid Id);
    private sealed record ProblemDetailsBody(string Type);
}
```

- [ ] **Step 7: Run tests to verify they fail**

Run: `dotnet test tests/SnagList.Api.Tests --filter SnagEndpointsTests`
Expected: FAIL — `SnagEndpoints`/`MapSnagEndpoints` don't exist yet.

- [ ] **Step 8: Implement the endpoints**

```csharp
namespace SnagList.Api.Endpoints;

using Microsoft.AspNetCore.Mvc;
using SnagList.Api.Authorization;
using SnagList.Api.Contracts;
using SnagList.Api.Contracts.Snags;
using SnagList.Api.Hypermedia;
using SnagList.Application.Snags.Commands;
using SnagList.Application.Snags.Queries;
using SnagList.Domain.Snags;

public static class SnagEndpoints
{
    public static IEndpointRouteBuilder MapSnagEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/snags").RequireAuthorization(PolicyNames.Staff);

        group.MapPost("", async (
            ReportSnagRequest request, ReportSnagCommandHandler handler, HttpContext http, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(new ReportSnagCommand(
                request.LocationId, request.SubLocation, request.Category, request.Severity,
                request.Description, http.User.GetStaffId(), http.User.GetStaffName()), ct);
            return Results.Created($"/api/v1/snags/{id}", new { id });
        }).WithName("ReportSnag");

        group.MapGet("", async (
            [FromQuery] Guid? locationId, [FromQuery] SnagCategory? category, [FromQuery] SnagSeverity? severity,
            [FromQuery] SnagStatus? status, [FromQuery] string? cursor, [FromQuery] int limit,
            ListSnagsQueryHandler handler, CancellationToken ct) =>
        {
            var page = await handler.HandleAsync(
                new ListSnagsQuery(locationId, category, severity, status, cursor, limit is > 0 and <= 100 ? limit : 20), ct);
            var items = page.Items.Select(s => new SnagSummaryResponse
            {
                Id = s.Id,
                LocationId = s.LocationId,
                SubLocation = s.SubLocation,
                Category = s.Category,
                Severity = s.Severity,
                Status = s.Status,
                ReportedByName = s.ReportedByName,
                ReportedAt = s.ReportedAt,
                Version = s.Version,
                Links = SnagLinksBuilder.BuildSummaryLinks(s.Id),
            }).ToList();
            return Results.Ok(new PagedResponse<SnagSummaryResponse>(items, page.NextCursor));
        }).WithName("ListSnags");

        group.MapGet("/{id:guid}", async (Guid id, GetSnagQueryHandler handler, HttpContext http, CancellationToken ct) =>
        {
            var detail = await handler.HandleAsync(new GetSnagQuery(id), ct);
            return detail is null ? Results.NotFound() : Results.Ok(ToDetailResponse(detail, http));
        }).WithName("GetSnag");

        group.MapPatch("/{id:guid}", async (
            Guid id, EditSnagRequest request, EditSnagCommandHandler handler, HttpContext http, CancellationToken ct) =>
        {
            await handler.HandleAsync(new EditSnagCommand(
                id, http.User.GetStaffId(), request.SubLocation, request.Category, request.Severity,
                request.Description, request.ExpectedVersion), ct);
            return Results.NoContent();
        }).WithName("EditSnag");

        group.MapPost("/{id:guid}/withdraw", async (
            Guid id, WithdrawSnagRequest request, WithdrawSnagCommandHandler handler, HttpContext http, CancellationToken ct) =>
        {
            await handler.HandleAsync(new WithdrawSnagCommand(id, http.User.GetStaffId(), request.ExpectedVersion), ct);
            return Results.NoContent();
        }).WithName("WithdrawSnag");

        return app;
    }

    internal static SnagDetailResponse ToDetailResponse(SnagDetail detail, HttpContext http) => new()
    {
        Id = detail.Id,
        LocationId = detail.LocationId,
        SubLocation = detail.SubLocation,
        Category = detail.Category,
        Severity = detail.Severity,
        Description = detail.Description,
        Status = detail.Status,
        ReportedByStaffId = detail.ReportedByStaffId,
        ReportedByName = detail.ReportedByName,
        ReportedAt = detail.ReportedAt,
        Version = detail.Version,
        Comments = detail.Comments
            .Select(c => new SnagCommentResponse(c.Id, c.AuthorStaffId, c.AuthorName, c.Body, c.CreatedAt))
            .ToList(),
        Photos = detail.Photos.Select(p =>
        {
            var photoKey = PhotoKeyFrom(detail.Id, p.BlobKey);
            return new SnagPhotoResponse(photoKey, p.FileName, p.ContentType, p.SizeBytes, p.UploadedAt,
                new ApiLink($"/api/v1/snags/{detail.Id}/photos/{photoKey}", "GET", "GetSnagPhoto"));
        }).ToList(),
        Links = SnagLinksBuilder.Build(detail.Id, detail.Status, detail.ReportedByStaffId, http.User.GetStaffId(), http.User.GetStaffRoles()),
    };

    internal static string PhotoKeyFrom(Guid snagId, string blobKey) => blobKey[$"snags/{snagId}/".Length..];
}
```

- [ ] **Step 9: Wire it into `Program.cs`**

Modify `src/SnagList.Api/Program.cs`: register the handlers —

```csharp
builder.Services.AddScoped<ReportSnagCommandHandler>();
builder.Services.AddScoped<EditSnagCommandHandler>();
builder.Services.AddScoped<WithdrawSnagCommandHandler>();
builder.Services.AddScoped<ListSnagsQueryHandler>();
builder.Services.AddScoped<GetSnagQueryHandler>();
```

— and add `app.MapSnagEndpoints();` next to `app.MapLocationEndpoints();`.

- [ ] **Step 10: Run tests to verify they pass**

Run: `dotnet test tests/SnagList.Api.Tests --filter SnagEndpointsTests`
Expected: PASS (5 tests).

- [ ] **Step 11: Commit**

```bash
git add src/SnagList.Api tests/SnagList.Api.Tests
git commit -m "feat(api): add Snag report/get/list/edit/withdraw endpoints"
```

---

## Task 19: Api — status-transition, comment, and photo endpoints

**Files:**
- Create: `src/SnagList.Api/Contracts/Snags/SnagLifecycleRequests.cs`
- Modify: `src/SnagList.Api/Endpoints/SnagEndpoints.cs` — add the transition/comment/photo routes
- Modify: `tests/SnagList.Api.Tests/Testing/SnagListApiFactory.cs` — add
  `CreateAuthenticatedClientNoRedirect`, needed to assert on the photo-download 302 without the
  `HttpClient` silently following it
- Modify: `src/SnagList.Api/Program.cs` — register the remaining command handlers
- Test: `tests/SnagList.Api.Tests/Endpoints/SnagLifecycleEndpointsTests.cs`

**Interfaces:**
- Consumes: `ChangeSnagStatusCommandHandler`, `RejectSnagCommandHandler` (Task 8),
  `AddSnagCommentCommandHandler`, `UploadSnagPhotoCommandHandler` (Task 9), `IBlobStorage` (Task 5,
  13), `SnagEndpoints.PhotoKeyFrom` (Task 18).
- Produces: the remaining five REST operations from 03-api-design.md's resource table — nothing
  further in Api consumes these; they're the last piece before the OpenAPI/agent decoration in
  Task 20.

- [ ] **Step 1: Write the request contracts**

```csharp
namespace SnagList.Api.Contracts.Snags;

public sealed record ChangeSnagStatusRequest(int ExpectedVersion);
public sealed record RejectSnagRequest(string Reason, int ExpectedVersion);
public sealed record AddSnagCommentRequest(string Body);
```

- [ ] **Step 2: Add `CreateAuthenticatedClientNoRedirect` to the shared factory**

```csharp
// Add to SnagListApiFactory (tests/SnagList.Api.Tests/Testing/SnagListApiFactory.cs)

public HttpClient CreateAuthenticatedClientNoRedirect(string staffId, params string[] roles)
{
    var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    client.DefaultRequestHeaders.Add("X-Test-StaffId", staffId);
    client.DefaultRequestHeaders.Add("X-Test-Roles", string.Join(',', roles));
    return client;
}
```

- [ ] **Step 3: Write the failing tests**

```csharp
namespace SnagList.Api.Tests.Endpoints;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SnagList.Api.Contracts.Locations;
using SnagList.Api.Contracts.Snags;
using SnagList.Api.Tests.Testing;
using SnagList.Domain.Snags;
using Xunit;

public class SnagLifecycleEndpointsTests(SnagListApiFactory factory) : IClassFixture<SnagListApiFactory>
{
    private sealed record CreatedResource(Guid Id);

    private async Task<Guid> CreateLocationAsync(string name)
    {
        var client = factory.CreateAuthenticatedClient("U900010", "Maintenance");
        var response = await client.PostAsJsonAsync("/api/v1/locations", new CreateLocationRequest(name, "1 Main St"));
        return (await response.Content.ReadFromJsonAsync<CreatedResource>())!.Id;
    }

    private async Task<Guid> ReportSnagAsync(Guid locationId, string reporterStaffId)
    {
        var reporter = factory.CreateAuthenticatedClient(reporterStaffId, "Staff");
        var response = await reporter.PostAsJsonAsync("/api/v1/snags",
            new ReportSnagRequest(locationId, "3rd floor", SnagCategory.Electrical, SnagSeverity.Medium, "desc"));
        return (await response.Content.ReadFromJsonAsync<CreatedResource>())!.Id;
    }

    [Fact]
    public async Task Maintenance_can_drive_a_Snag_through_its_full_lifecycle()
    {
        var locationId = await CreateLocationAsync("Head Office F");
        var snagId = await ReportSnagAsync(locationId, "U100020");
        var maintenance = factory.CreateAuthenticatedClient("U900011", "Maintenance");

        Assert.Equal(HttpStatusCode.NoContent,
            (await maintenance.PostAsJsonAsync($"/api/v1/snags/{snagId}/acknowledge", new ChangeSnagStatusRequest(1))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await maintenance.PostAsJsonAsync($"/api/v1/snags/{snagId}/start", new ChangeSnagStatusRequest(2))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await maintenance.PostAsJsonAsync($"/api/v1/snags/{snagId}/resolve", new ChangeSnagStatusRequest(3))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await maintenance.PostAsJsonAsync($"/api/v1/snags/{snagId}/close", new ChangeSnagStatusRequest(4))).StatusCode);

        var detail = await maintenance.GetFromJsonAsync<SnagDetailResponse>($"/api/v1/snags/{snagId}");
        Assert.Equal(SnagStatus.Closed, detail!.Status);
    }

    [Fact]
    public async Task Staff_cannot_acknowledge_a_Snag()
    {
        var locationId = await CreateLocationAsync("Head Office G");
        var snagId = await ReportSnagAsync(locationId, "U100021");
        var staff = factory.CreateAuthenticatedClient("U100021", "Staff");

        var response = await staff.PostAsJsonAsync($"/api/v1/snags/{snagId}/acknowledge", new ChangeSnagStatusRequest(1));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Maintenance_can_reject_with_a_reason_recorded_as_a_comment()
    {
        var locationId = await CreateLocationAsync("Head Office H");
        var snagId = await ReportSnagAsync(locationId, "U100022");
        var maintenance = factory.CreateAuthenticatedClient("U900012", "Maintenance");

        var response = await maintenance.PostAsJsonAsync($"/api/v1/snags/{snagId}/reject", new RejectSnagRequest("Duplicate report", 1));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var detail = await maintenance.GetFromJsonAsync<SnagDetailResponse>($"/api/v1/snags/{snagId}");
        Assert.Equal(SnagStatus.Rejected, detail!.Status);
        Assert.Contains(detail.Comments, c => c.Body == "Duplicate report");
    }

    [Fact]
    public async Task Either_role_can_add_a_comment()
    {
        var locationId = await CreateLocationAsync("Head Office I");
        var snagId = await ReportSnagAsync(locationId, "U100023");
        var reporter = factory.CreateAuthenticatedClient("U100023", "Staff");

        var response = await reporter.PostAsJsonAsync($"/api/v1/snags/{snagId}/comments", new AddSnagCommentRequest("Any update?"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Uploading_a_photo_then_downloading_it_redirects_to_a_presigned_url()
    {
        var locationId = await CreateLocationAsync("Head Office J");
        var snagId = await ReportSnagAsync(locationId, "U100024");
        var reporter = factory.CreateAuthenticatedClient("U100024", "Staff");

        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent([1, 2, 3, 4]);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(fileContent, "file", "light.jpg");
        var uploadResponse = await reporter.PostAsync($"/api/v1/snags/{snagId}/photos", form);
        Assert.Equal(HttpStatusCode.NoContent, uploadResponse.StatusCode);

        var detail = await reporter.GetFromJsonAsync<SnagDetailResponse>($"/api/v1/snags/{snagId}");
        var photo = Assert.Single(detail!.Photos);

        var noRedirectClient = factory.CreateAuthenticatedClientNoRedirect("U100024", "Staff");
        var downloadResponse = await noRedirectClient.GetAsync(photo.Href.Href);

        Assert.Equal(HttpStatusCode.Redirect, downloadResponse.StatusCode);
        Assert.Contains(photo.PhotoKey, downloadResponse.Headers.Location!.ToString());
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test tests/SnagList.Api.Tests --filter SnagLifecycleEndpointsTests`
Expected: FAIL — the routes don't exist yet.

- [ ] **Step 5: Add the routes to `SnagEndpoints.MapSnagEndpoints`**

```csharp
// Insert into MapSnagEndpoints (src/SnagList.Api/Endpoints/SnagEndpoints.cs), before `return app;`

Task<IResult> ChangeStatus(Guid id, ChangeSnagStatusRequest request, SnagStatus target,
    ChangeSnagStatusCommandHandler handler, HttpContext http, CancellationToken ct) =>
    handler.HandleAsync(new ChangeSnagStatusCommand(id, target, http.User.GetStaffId(), request.ExpectedVersion), ct)
        .ContinueWith(_ => Results.NoContent(), ct);

group.MapPost("/{id:guid}/acknowledge", (Guid id, ChangeSnagStatusRequest request, ChangeSnagStatusCommandHandler handler, HttpContext http, CancellationToken ct) =>
        ChangeStatus(id, request, SnagStatus.Acknowledged, handler, http, ct))
    .RequireAuthorization(PolicyNames.Maintenance).WithName("AcknowledgeSnag");

group.MapPost("/{id:guid}/start", (Guid id, ChangeSnagStatusRequest request, ChangeSnagStatusCommandHandler handler, HttpContext http, CancellationToken ct) =>
        ChangeStatus(id, request, SnagStatus.InProgress, handler, http, ct))
    .RequireAuthorization(PolicyNames.Maintenance).WithName("StartSnagWork");

group.MapPost("/{id:guid}/resolve", (Guid id, ChangeSnagStatusRequest request, ChangeSnagStatusCommandHandler handler, HttpContext http, CancellationToken ct) =>
        ChangeStatus(id, request, SnagStatus.Resolved, handler, http, ct))
    .RequireAuthorization(PolicyNames.Maintenance).WithName("ResolveSnag");

group.MapPost("/{id:guid}/close", (Guid id, ChangeSnagStatusRequest request, ChangeSnagStatusCommandHandler handler, HttpContext http, CancellationToken ct) =>
        ChangeStatus(id, request, SnagStatus.Closed, handler, http, ct))
    .RequireAuthorization(PolicyNames.Maintenance).WithName("CloseSnag");

group.MapPost("/{id:guid}/reject", async (Guid id, RejectSnagRequest request, RejectSnagCommandHandler handler, HttpContext http, CancellationToken ct) =>
    {
        await handler.HandleAsync(new RejectSnagCommand(
            id, http.User.GetStaffId(), http.User.GetStaffName(), request.Reason, request.ExpectedVersion), ct);
        return Results.NoContent();
    })
    .RequireAuthorization(PolicyNames.Maintenance).WithName("RejectSnag");

group.MapPost("/{id:guid}/comments", async (Guid id, AddSnagCommentRequest request, AddSnagCommentCommandHandler handler, HttpContext http, CancellationToken ct) =>
{
    await handler.HandleAsync(new AddSnagCommentCommand(id, http.User.GetStaffId(), http.User.GetStaffName(), request.Body), ct);
    return Results.NoContent();
}).WithName("AddSnagComment");

group.MapPost("/{id:guid}/photos", async (Guid id, HttpRequest httpRequest, UploadSnagPhotoCommandHandler handler, CancellationToken ct) =>
{
    if (!httpRequest.HasFormContentType) return Results.BadRequest();
    var form = await httpRequest.ReadFormAsync(ct);
    var file = form.Files.GetFile("file");
    if (file is null) return Results.BadRequest();

    await using var stream = file.OpenReadStream();
    await handler.HandleAsync(new UploadSnagPhotoCommand(id, file.FileName, file.ContentType, stream, file.Length), ct);
    return Results.NoContent();
}).WithName("UploadSnagPhoto").DisableAntiforgery();

group.MapGet("/{id:guid}/photos/{photoKey}", async (
    Guid id, string photoKey, GetSnagQueryHandler getSnag, SnagList.Application.Abstractions.IBlobStorage blobStorage, CancellationToken ct) =>
{
    var detail = await getSnag.HandleAsync(new GetSnagQuery(id), ct);
    if (detail is null) return Results.NotFound();

    var blobKey = $"snags/{id}/{photoKey}";
    if (!detail.Photos.Any(p => p.BlobKey == blobKey)) return Results.NotFound();

    var url = await blobStorage.GetPresignedGetUrlAsync(blobKey, TimeSpan.FromMinutes(10), ct);
    return Results.Redirect(url.ToString());
}).WithName("GetSnagPhoto");
```

Note the local `ChangeStatus` function is declared once and reused by the four structurally
identical transition routes — this mirrors the `ChangeSnagStatusCommand` sharing decision from
Task 8 at the endpoint layer too, rather than four copy-pasted lambda bodies.

- [ ] **Step 6: Wire the remaining handlers into `Program.cs`**

```csharp
builder.Services.AddScoped<ChangeSnagStatusCommandHandler>();
builder.Services.AddScoped<RejectSnagCommandHandler>();
builder.Services.AddScoped<AddSnagCommentCommandHandler>();
builder.Services.AddScoped<UploadSnagPhotoCommandHandler>();
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/SnagList.Api.Tests --filter SnagLifecycleEndpointsTests`
Expected: PASS (5 tests).

Run: `dotnet test tests/SnagList.Api.Tests`
Expected: PASS — every Api test from Tasks 15–19 is green.

- [ ] **Step 8: Commit**

```bash
git add src/SnagList.Api tests/SnagList.Api.Tests
git commit -m "feat(api): add Snag status-transition, comment, and photo endpoints"
```

---

## Task 20: Api — `Me` endpoint, `StaffIdentity` sync middleware, and agent-friendly OpenAPI decoration

Completes the REST surface. The `StaffIdentity` sync runs on *every* authenticated request (not
just `/me`), per 04-security-and-authentication.md — it's a display/audit cache, refreshed
opportunistically, never consulted for authorization.

**Files:**
- Create: `src/SnagList.Application/Abstractions/IStaffIdentityRepository.cs`
- Create: `src/SnagList.Application/Staff/Commands/SyncStaffIdentityCommand.cs`
- Create: `src/SnagList.Infrastructure/Persistence/Repositories/EfStaffIdentityRepository.cs`
- Create: `src/SnagList.Api/Middleware/StaffIdentitySyncMiddleware.cs`
- Create: `src/SnagList.Api/Endpoints/MeEndpoints.cs`
- Create: `src/SnagList.Api/OpenApi/AgentOperationCatalog.cs`
- Create: `src/SnagList.Api/OpenApi/AgentHintsDocumentTransformer.cs`
- Modify: `src/SnagList.Api/Program.cs` — register the new port/handler, the sync middleware, the
  `Me` endpoint, and the OpenAPI document transformer
- Test: `tests/SnagList.Application.Tests/Testing/FakeStaffIdentityRepository.cs`
- Test: `tests/SnagList.Application.Tests/Staff/SyncStaffIdentityCommandTests.cs`
- Test: `tests/SnagList.Api.Tests/Endpoints/MeEndpointTests.cs`
- Test: `tests/SnagList.Api.Tests/OpenApi/AgentHintsDocumentTransformerTests.cs`

**Interfaces:**
- Produces: `AgentOperationCatalog.OperationIdToMcpTool` — the single source of truth a later MCP
  server plan builds its tool registrations from and CI-checks for parity against, per
  03-api-design.md.

- [ ] **Step 1: Write the failing `SyncStaffIdentityCommand` tests**

```csharp
namespace SnagList.Application.Tests.Testing;

using SnagList.Application.Abstractions;
using SnagList.Domain.Staff;

public sealed class FakeStaffIdentityRepository : IStaffIdentityRepository
{
    public readonly Dictionary<string, StaffIdentity> Store = [];

    public Task<StaffIdentity?> GetAsync(string staffId, CancellationToken ct) =>
        Task.FromResult(Store.GetValueOrDefault(staffId));

    public void Add(StaffIdentity identity) => Store[identity.StaffId] = identity;
}
```

```csharp
namespace SnagList.Application.Tests.Staff;

using SnagList.Application.Staff.Commands;
using SnagList.Application.Tests.Testing;
using SnagList.Domain.Staff;
using Xunit;

public class SyncStaffIdentityCommandTests
{
    [Fact]
    public async Task First_sight_creates_a_new_StaffIdentity()
    {
        var repo = new FakeStaffIdentityRepository();
        var handler = new SyncStaffIdentityCommandHandler(repo, new FakeUnitOfWork(), new FakeClock(DateTimeOffset.UtcNow));

        await handler.HandleAsync(new SyncStaffIdentityCommand("U123456", "Jane Smith", "jane@example.com", [StaffRole.Staff]), default);

        Assert.True(repo.Store.ContainsKey("U123456"));
    }

    [Fact]
    public async Task A_second_sight_syncs_the_existing_row_instead_of_duplicating()
    {
        var repo = new FakeStaffIdentityRepository();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var handler = new SyncStaffIdentityCommandHandler(repo, new FakeUnitOfWork(), clock);
        await handler.HandleAsync(new SyncStaffIdentityCommand("U123456", "Jane Smith", "jane@example.com", [StaffRole.Staff]), default);

        clock.UtcNow = clock.UtcNow.AddDays(1);
        await handler.HandleAsync(
            new SyncStaffIdentityCommand("U123456", "Jane Smith", "jane@example.com", [StaffRole.Staff, StaffRole.Maintenance]),
            default);

        Assert.Single(repo.Store);
        Assert.Equal([StaffRole.Staff, StaffRole.Maintenance], repo.Store["U123456"].Roles);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/SnagList.Application.Tests --filter SyncStaffIdentityCommandTests`
Expected: FAIL — the types don't exist yet.

- [ ] **Step 3: Implement the port, command, and EF repository**

```csharp
namespace SnagList.Application.Abstractions;

using SnagList.Domain.Staff;

public interface IStaffIdentityRepository
{
    Task<StaffIdentity?> GetAsync(string staffId, CancellationToken ct);
    void Add(StaffIdentity identity);
}
```

```csharp
namespace SnagList.Application.Staff.Commands;

using SnagList.Application.Abstractions;
using SnagList.Domain.Staff;

public sealed record SyncStaffIdentityCommand(string StaffId, string Name, string Email, IReadOnlyList<StaffRole> Roles);

public sealed class SyncStaffIdentityCommandHandler(IStaffIdentityRepository repository, IUnitOfWork unitOfWork, IClock clock)
{
    public async Task HandleAsync(SyncStaffIdentityCommand command, CancellationToken ct)
    {
        var existing = await repository.GetAsync(command.StaffId, ct);
        if (existing is null)
        {
            repository.Add(StaffIdentity.FirstSeen(command.StaffId, command.Name, command.Email, command.Roles, clock.UtcNow));
        }
        else
        {
            existing.Sync(command.Name, command.Email, command.Roles, clock.UtcNow);
        }
        await unitOfWork.SaveChangesAsync(ct);
    }
}
```

```csharp
namespace SnagList.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using SnagList.Application.Abstractions;
using SnagList.Domain.Staff;

public sealed class EfStaffIdentityRepository(SnagListDbContext dbContext) : IStaffIdentityRepository
{
    public Task<StaffIdentity?> GetAsync(string staffId, CancellationToken ct) =>
        dbContext.StaffIdentities.FirstOrDefaultAsync(s => s.StaffId == staffId, ct);

    public void Add(StaffIdentity identity) => dbContext.StaffIdentities.Add(identity);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/SnagList.Application.Tests --filter SyncStaffIdentityCommandTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Write the sync middleware, `Me` endpoint, and the failing Api tests**

```csharp
namespace SnagList.Api.Middleware;

using SnagList.Api.Authorization;
using SnagList.Application.Staff.Commands;

public sealed class StaffIdentitySyncMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, SyncStaffIdentityCommandHandler handler)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            await handler.HandleAsync(new SyncStaffIdentityCommand(
                context.User.GetStaffId(), context.User.GetStaffName(), context.User.GetStaffEmail(),
                context.User.GetStaffRoles()), context.RequestAborted);
        }
        await next(context);
    }
}
```

```csharp
namespace SnagList.Api.Endpoints;

using SnagList.Api.Authorization;
using SnagList.Domain.Staff;

public static class MeEndpoints
{
    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/me", (HttpContext http) => Results.Ok(new MeResponse(
                http.User.GetStaffId(), http.User.GetStaffName(), http.User.GetStaffEmail(), http.User.GetStaffRoles())))
            .RequireAuthorization(PolicyNames.Staff)
            .WithName("GetMe");
        return app;
    }
}

public sealed record MeResponse(string StaffId, string Name, string Email, IReadOnlyList<StaffRole> Roles);
```

```csharp
namespace SnagList.Api.Tests.Endpoints;

using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SnagList.Api.Endpoints;
using SnagList.Api.Tests.Testing;
using SnagList.Infrastructure.Persistence;
using Xunit;

public class MeEndpointTests(SnagListApiFactory factory) : IClassFixture<SnagListApiFactory>
{
    [Fact]
    public async Task Returns_the_callers_identity_and_roles()
    {
        var client = factory.CreateAuthenticatedClient("U100030", "Staff", "Maintenance");

        var me = await client.GetFromJsonAsync<MeResponse>("/api/v1/me");

        Assert.Equal("U100030", me!.StaffId);
        Assert.Contains(SnagList.Domain.Staff.StaffRole.Maintenance, me.Roles);
    }

    [Fact]
    public async Task Syncs_a_StaffIdentity_row_on_first_authenticated_request()
    {
        var client = factory.CreateAuthenticatedClient("U100031", "Staff");

        await client.GetAsync("/api/v1/me");

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SnagListDbContext>();
        var identity = await dbContext.StaffIdentities.FirstOrDefaultAsync(s => s.StaffId == "U100031");
        Assert.NotNull(identity);
    }
}
```

- [ ] **Step 6: Run tests to verify they fail**

Run: `dotnet test tests/SnagList.Api.Tests --filter MeEndpointTests`
Expected: FAIL — `MeEndpoints`/`StaffIdentitySyncMiddleware` don't exist yet.

- [ ] **Step 7: Write the failing `AgentHintsDocumentTransformer` test**

```csharp
namespace SnagList.Api.Tests.OpenApi;

using System.Net.Http.Json;
using System.Text.Json;
using SnagList.Api.Tests.Testing;
using Xunit;

public class AgentHintsDocumentTransformerTests(SnagListApiFactory factory) : IClassFixture<SnagListApiFactory>
{
    [Fact]
    public async Task GetSnag_operation_carries_the_x_mcp_tool_extension()
    {
        var document = await factory.CreateClient().GetFromJsonAsync<JsonElement>("/openapi/v1.json");

        var getSnagOperation = document.GetProperty("paths").GetProperty("/api/v1/snags/{id}").GetProperty("get");
        Assert.Equal("get_snag", getSnagOperation.GetProperty("x-mcp-tool").GetString());
    }
}
```

- [ ] **Step 8: Run test to verify it fails**

Run: `dotnet test tests/SnagList.Api.Tests --filter AgentHintsDocumentTransformerTests`
Expected: FAIL — no `x-mcp-tool` extension is present yet.

- [ ] **Step 9: Implement the operation catalog and document transformer**

```csharp
namespace SnagList.Api.OpenApi;

public static class AgentOperationCatalog
{
    public static readonly IReadOnlyDictionary<string, string> OperationIdToMcpTool = new Dictionary<string, string>
    {
        ["ReportSnag"] = "report_snag",
        ["EditSnag"] = "edit_snag",
        ["WithdrawSnag"] = "withdraw_snag",
        ["AcknowledgeSnag"] = "acknowledge_snag",
        ["StartSnagWork"] = "start_snag_work",
        ["ResolveSnag"] = "resolve_snag",
        ["CloseSnag"] = "close_snag",
        ["RejectSnag"] = "reject_snag",
        ["AddSnagComment"] = "add_snag_comment",
        ["UploadSnagPhoto"] = "upload_snag_photo",
        ["GetSnagPhoto"] = "get_snag_photo",
        ["ListSnags"] = "list_snags",
        ["GetSnag"] = "get_snag",
        ["ListLocations"] = "list_locations",
        ["GetLocation"] = "get_location",
        ["CreateLocation"] = "create_location",
        ["UpdateLocation"] = "update_location",
        ["RetireLocation"] = "retire_location",
        ["GetMe"] = "get_me",
    };
}
```

```csharp
namespace SnagList.Api.OpenApi;

using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;

public sealed class AgentHintsDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken ct)
    {
        foreach (var path in document.Paths.Values)
        {
            foreach (var operation in path.Operations.Values)
            {
                if (operation.OperationId is null) continue;
                if (!AgentOperationCatalog.OperationIdToMcpTool.TryGetValue(operation.OperationId, out var toolName)) continue;

                operation.Extensions["x-mcp-tool"] = new OpenApiString(toolName);
                operation.Extensions["x-agent-hints"] = new OpenApiString(
                    $"Corresponds 1:1 to the '{toolName}' MCP tool; the same authorization applies to both.");
            }
        }
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 10: Wire everything into `Program.cs`**

```csharp
builder.Services.AddScoped<IStaffIdentityRepository, EfStaffIdentityRepository>();
builder.Services.AddScoped<SyncStaffIdentityCommandHandler>();
builder.Services.AddOpenApi(options => options.AddDocumentTransformer<AgentHintsDocumentTransformer>());
```

— and, in the pipeline, after `app.UseAuthorization();`:

```csharp
app.UseMiddleware<StaffIdentitySyncMiddleware>();
```

— and, alongside the other `app.MapXEndpoints();` calls:

```csharp
app.MapMeEndpoints();
```

Every REST endpoint mapped across Tasks 17–20 must also carry an explicit `.WithName("...")`
matching its key in `AgentOperationCatalog` — Minimal APIs use `WithName` as the OperationId source,
and this transformer only decorates operations it recognises there. Cross-check now: every
`.WithName(...)` call written in Tasks 17–19 already matches a catalog key above.

- [ ] **Step 11: Run tests to verify they pass**

Run: `dotnet test tests/SnagList.Api.Tests`
Expected: PASS — the full Api test suite (Tasks 15–20) is green. This is the Api layer's exit
gate — every REST operation from 03-api-design.md now exists, is authorized, is hypermedia-linked,
and is agent-decorated.

- [ ] **Step 12: Commit**

```bash
git add src/SnagList.Application/Abstractions/IStaffIdentityRepository.cs src/SnagList.Application/Staff \
  src/SnagList.Infrastructure/Persistence/Repositories/EfStaffIdentityRepository.cs \
  src/SnagList.Api/Middleware src/SnagList.Api/Endpoints/MeEndpoints.cs src/SnagList.Api/OpenApi src/SnagList.Api/Program.cs \
  tests/SnagList.Application.Tests/Testing/FakeStaffIdentityRepository.cs tests/SnagList.Application.Tests/Staff \
  tests/SnagList.Api.Tests/Endpoints/MeEndpointTests.cs tests/SnagList.Api.Tests/OpenApi
git commit -m "feat(api): add Me endpoint, StaffIdentity sync, and agent-friendly OpenAPI decoration"
```

---

## Task 21: `SnagList.SeedData` — migrations runner and idempotent demo seed

Keycloak realm reconciliation (the other half of JointBooking's "seed" convergence step) is
deliberately **not** built here — local docker's Keycloak container imports its realm fresh from
`deploy/keycloak/realm-export.json` on every start (Task 22), so there's nothing to reconcile yet.
That reconciliation only becomes necessary once a *persistent, shared* Keycloak instance exists,
which is the home-lab deployment plan's concern, not this one's.

**Files:**
- Create: `src/SnagList.SeedData/SeedRunner.cs`
- Create: `src/SnagList.SeedData/Program.cs`
- Test: `tests/SnagList.SeedData.Tests/PostgresFixture.cs`
- Test: `tests/SnagList.SeedData.Tests/SeedRunnerTests.cs`

**Interfaces:**
- Consumes: `SnagListDbContext` (Task 11); `Location.Create`, `Snag.Report`, `snag.TransitionTo`
  (Tasks 2–3).
- Produces: `SeedRunner.RunAsync(dbContext, output)` — called from `Program.cs`, and from the
  `seed` compose service's entrypoint in Task 22.

- [ ] **Step 1: Write the failing tests**

```csharp
namespace SnagList.SeedData.Tests;

using Microsoft.EntityFrameworkCore;
using SnagList.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();

    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public SnagListDbContext CreateContext() => new(
        new DbContextOptionsBuilder<SnagListDbContext>().UseNpgsql(_container.GetConnectionString()).Options);
}
```

```csharp
namespace SnagList.SeedData.Tests;

using Microsoft.EntityFrameworkCore;
using Xunit;

public class SeedRunnerTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task RunAsync_migrates_and_seeds_demo_data()
    {
        await using var dbContext = fixture.CreateContext();

        await SeedRunner.RunAsync(dbContext, TextWriter.Null);

        Assert.Equal(3, await dbContext.Locations.CountAsync());
        Assert.Equal(2, await dbContext.Snags.CountAsync());
    }

    [Fact]
    public async Task RunAsync_is_idempotent_across_repeated_runs()
    {
        await using (var firstRun = fixture.CreateContext()) await SeedRunner.RunAsync(firstRun, TextWriter.Null);
        await using var secondRun = fixture.CreateContext();

        await SeedRunner.RunAsync(secondRun, TextWriter.Null);

        Assert.Equal(3, await secondRun.Locations.CountAsync());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/SnagList.SeedData.Tests --filter SeedRunnerTests`
Expected: FAIL — `SeedRunner` does not exist.

- [ ] **Step 3: Implement `SeedRunner`**

```csharp
namespace SnagList.SeedData;

using Microsoft.EntityFrameworkCore;
using SnagList.Domain.Locations;
using SnagList.Domain.Snags;
using SnagList.Infrastructure.Persistence;

public static class SeedRunner
{
    public static async Task RunAsync(SnagListDbContext dbContext, TextWriter output)
    {
        await dbContext.Database.MigrateAsync();

        if (await dbContext.Locations.AnyAsync())
        {
            output.WriteLine("Demo data already present; skipping seed.");
            return;
        }

        output.WriteLine("Seeding demo data...");
        var headOffice = Location.Create("Head Office", "1 Main St, London");
        var northernOffice = Location.Create("Northern Office", "42 North Rd, Leeds");
        var engineeringSite = Location.Create("Engineering Site", "3 Park Rd, Manchester");
        dbContext.Locations.AddRange(headOffice, northernOffice, engineeringSite);

        var snag1 = Snag.Report(
            headOffice.Id, "3rd floor, room 3.12", SnagCategory.Electrical, SnagSeverity.Medium,
            "Flickering light above the kitchenette", "U100001", "Jane Smith", DateTimeOffset.UtcNow.AddDays(-3));
        var snag2 = Snag.Report(
            northernOffice.Id, "Ground floor reception", SnagCategory.HeatingAndCooling, SnagSeverity.High,
            "Reception is freezing, heating not working", "U100002", "Tom Brown", DateTimeOffset.UtcNow.AddDays(-1));
        snag2.TransitionTo(SnagStatus.Acknowledged, "U900001", DateTimeOffset.UtcNow.AddHours(-12));
        dbContext.Snags.AddRange(snag1, snag2);

        await dbContext.SaveChangesAsync();
        output.WriteLine("Seed complete.");
    }
}
```

```csharp
using Microsoft.EntityFrameworkCore;
using SnagList.Infrastructure.Persistence;
using SnagList.SeedData;

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__SnagList")
    ?? throw new InvalidOperationException("ConnectionStrings__SnagList is required.");
var options = new DbContextOptionsBuilder<SnagListDbContext>().UseNpgsql(connectionString).Options;
await using var dbContext = new SnagListDbContext(options);

await SeedRunner.RunAsync(dbContext, Console.Out);
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/SnagList.SeedData.Tests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/SnagList.SeedData tests/SnagList.SeedData.Tests
git commit -m "feat(seed-data): add migrations runner and idempotent demo seed"
```

---

## Task 22: Local docker deployment — `docker-compose.yml`, Dockerfiles, and the Keycloak realm export

The exit gate for this entire plan: everything from Tasks 1–21, running together in the local
docker stack described in 05-deployment-local-docker.md.

**Files:**
- Create: `src/SnagList.Api/Dockerfile`
- Create: `src/SnagList.SeedData/Dockerfile`
- Create: `deploy/keycloak/realm-export.json`
- Create: `docker-compose.yml`

**Interfaces:** none — this task wires together every artifact the previous 21 tasks produced;
nothing further in this plan consumes it.

- [ ] **Step 1: Write the Dockerfiles**

```dockerfile
# src/SnagList.Api/Dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/SnagList.Api/SnagList.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "SnagList.Api.dll"]
```

```dockerfile
# src/SnagList.SeedData/Dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/SnagList.SeedData/SnagList.SeedData.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "SnagList.SeedData.dll"]
```

- [ ] **Step 2: Write the Keycloak realm export**

Two demo users so the manual verification in Step 5 can exercise both roles: `jane.smith` (`Staff`
only) and `bob.maintenance` (`Staff` + `Maintenance` — a Maintenance team member who can also
report, per 01-domain-model.md).

```json
{
  "realm": "snaglist",
  "enabled": true,
  "sslRequired": "none",
  "roles": {
    "realm": [
      { "name": "Staff", "description": "Can report and view Snags" },
      { "name": "Maintenance", "description": "Can triage, resolve, and manage Locations" }
    ]
  },
  "clients": [
    {
      "clientId": "snaglist-api",
      "enabled": true,
      "publicClient": false,
      "secret": "local-dev-secret",
      "protocol": "openid-connect",
      "standardFlowEnabled": true,
      "directAccessGrantsEnabled": true,
      "redirectUris": ["http://localhost:5080/*"],
      "webOrigins": ["*"],
      "protocolMappers": [
        {
          "name": "staff-id-mapper",
          "protocol": "openid-connect",
          "protocolMapper": "oidc-usermodel-attribute-mapper",
          "config": {
            "user.attribute": "staff_id",
            "claim.name": "staff_id",
            "jsonType.label": "String",
            "id.token.claim": "true",
            "access.token.claim": "true",
            "userinfo.token.claim": "true"
          }
        },
        {
          "name": "realm-roles-mapper",
          "protocol": "openid-connect",
          "protocolMapper": "oidc-usermodel-realm-role-mapper",
          "config": {
            "claim.name": "roles",
            "jsonType.label": "String",
            "multivalued": "true",
            "id.token.claim": "true",
            "access.token.claim": "true",
            "userinfo.token.claim": "true"
          }
        }
      ]
    }
  ],
  "users": [
    {
      "username": "jane.smith",
      "enabled": true,
      "email": "jane.smith@example.com",
      "firstName": "Jane",
      "lastName": "Smith",
      "credentials": [{ "type": "password", "value": "password", "temporary": false }],
      "attributes": { "staff_id": ["U100001"] },
      "realmRoles": ["Staff"]
    },
    {
      "username": "bob.maintenance",
      "enabled": true,
      "email": "bob.maintenance@example.com",
      "firstName": "Bob",
      "lastName": "Maintenance",
      "credentials": [{ "type": "password", "value": "password", "temporary": false }],
      "attributes": { "staff_id": ["U900001"] },
      "realmRoles": ["Staff", "Maintenance"]
    }
  ]
}
```

- [ ] **Step 3: Write `docker-compose.yml`**

```yaml
services:
  keycloak:
    image: quay.io/keycloak/keycloak:26.0
    command: start-dev --import-realm
    environment:
      KEYCLOAK_ADMIN: admin
      KEYCLOAK_ADMIN_PASSWORD: admin
    volumes:
      - ./deploy/keycloak/realm-export.json:/opt/keycloak/data/import/realm-export.json:ro
    ports:
      - "8080:8080"

  db:
    image: postgres:16-alpine
    environment:
      POSTGRES_DB: snaglist
      POSTGRES_USER: snaglist
      POSTGRES_PASSWORD: snaglist
    ports:
      - "5432:5432"
    volumes:
      - snaglist-db-data:/var/lib/postgresql/data

  minio:
    image: minio/minio:latest
    command: server /data --console-address ":9001"
    environment:
      MINIO_ROOT_USER: minioadmin
      MINIO_ROOT_PASSWORD: minioadmin
    ports:
      - "9000:9000"
      - "9001:9001"
    volumes:
      - snaglist-minio-data:/data

  minio-bootstrap:
    image: minio/mc:latest
    depends_on:
      - minio
    entrypoint: >
      /bin/sh -c "
      mc alias set local http://minio:9000 minioadmin minioadmin &&
      mc mb --ignore-existing local/snaglist-photos
      "

  mailpit:
    image: axllent/mailpit:latest
    ports:
      - "8025:8025"
      - "1025:1025"

  api:
    build:
      context: .
      dockerfile: src/SnagList.Api/Dockerfile
    depends_on:
      - db
      - minio
      - keycloak
    environment:
      ConnectionStrings__SnagList: "Host=db;Database=snaglist;Username=snaglist;Password=snaglist"
      Storage__BucketName: snaglist-photos
      Storage__AccessKey: minioadmin
      Storage__SecretKey: minioadmin
      Storage__ServiceUrl: http://minio:9000
      Email__Host: mailpit
      Email__Port: "1025"
      Email__FromAddress: snaglist@example.com
      Email__UseTls: "false"
      Auth__Local__Authority: http://keycloak:8080/realms/snaglist
      Auth__Local__Audience: snaglist-api
    ports:
      - "5080:8080"

  seed:
    build:
      context: .
      dockerfile: src/SnagList.SeedData/Dockerfile
    profiles: ["seed"]
    depends_on:
      - db
    environment:
      ConnectionStrings__SnagList: "Host=db;Database=snaglist;Username=snaglist;Password=snaglist"

volumes:
  snaglist-db-data:
  snaglist-minio-data:
```

- [ ] **Step 4: Bring the stack up and run the seed**

```bash
docker compose up -d --build
docker compose --profile seed run --rm seed
```

Expected: `db`, `minio`, `mailpit`, `keycloak`, and `api` report healthy/running; the seed job logs
"Seed complete." and exits 0.

- [ ] **Step 5: Manually verify end-to-end against the real stack**

```bash
curl -s http://localhost:5080/health
# {"status":"healthy"}

TOKEN=$(curl -s -X POST http://localhost:8080/realms/snaglist/protocol/openid-connect/token \
  -d grant_type=password -d client_id=snaglist-api -d client_secret=local-dev-secret \
  -d username=jane.smith -d password=password | jq -r .access_token)

curl -s http://localhost:5080/api/v1/me -H "Authorization: Bearer $TOKEN"
# {"staffId":"U100001","name":"Jane Smith","email":"jane.smith@example.com","roles":["Staff"]}

curl -s http://localhost:5080/api/v1/snags -H "Authorization: Bearer $TOKEN"
# the two demo Snags seeded in Task 21
```

Expected: all three respond as shown, confirming the full path — Keycloak-issued token → API
authentication/authorization → Postgres-backed data — works against the real running stack, not
just against test doubles.

- [ ] **Step 6: Commit**

```bash
git add src/SnagList.Api/Dockerfile src/SnagList.SeedData/Dockerfile deploy/keycloak/realm-export.json docker-compose.yml
git commit -m "feat(deploy): add local docker-compose stack and Keycloak realm export"
```

---

## Task 23: Notifications — dispatch `SnagReported`/`SnagStatusChanged` via email

Closes a gap this plan's self-review caught: 08-testing-and-nonfunctional.md put email
notifications in scope, and Tasks 6–14 built the `IEmailSender` port and its SMTP adapter, but
nothing ever actually dispatched a domain event through it. The fix is small: `EfUnitOfWork`
already sees every tracked `Snag` on every save (Task 12), so dispatch belongs there — one
modified class, not four rewritten command handlers.

**Files:**
- Create: `src/SnagList.Application/Notifications/NotificationOptions.cs`
- Create: `src/SnagList.Application/Notifications/SnagNotificationDispatcher.cs`
- Create: `tests/SnagList.Application.Tests/Testing/FakeEmailSender.cs`
- Test: `tests/SnagList.Application.Tests/Notifications/SnagNotificationDispatcherTests.cs`
- Modify: `src/SnagList.Infrastructure/Persistence/EfUnitOfWork.cs` — take a
  `SnagNotificationDispatcher` dependency; after a successful save, dispatch and clear each tracked
  `Snag`'s domain events
- Modify: `tests/SnagList.Infrastructure.Tests/Persistence/EfUnitOfWorkTests.cs` — the two
  `new EfUnitOfWork(...)` call sites need a dispatcher argument
- Modify: `src/SnagList.Api/Program.cs` — register `NotificationOptions` (bound from config) and
  `SnagNotificationDispatcher`

**Interfaces:**
- Consumes: `IEmailSender` (Task 5, 14), `IStaffIdentityRepository` (Task 20), `SnagReported`,
  `SnagStatusChanged` (Task 3, now carrying `ReportedByStaffId` — see Task 3's note above).
- Produces: nothing further downstream — this is the last piece 08-testing-and-nonfunctional.md
  calls for.

- [ ] **Step 1: Write the failing dispatcher tests**

```csharp
namespace SnagList.Application.Tests.Testing;

using SnagList.Application.Abstractions;

public sealed class FakeEmailSender : IEmailSender
{
    public readonly List<(string To, string Subject)> SentEmails = [];

    public Task SendAsync(string toAddress, string subject, string body, CancellationToken ct)
    {
        SentEmails.Add((toAddress, subject));
        return Task.CompletedTask;
    }
}
```

```csharp
namespace SnagList.Application.Tests.Notifications;

using SnagList.Application.Notifications;
using SnagList.Application.Tests.Testing;
using SnagList.Domain.Snags;
using SnagList.Domain.Snags.Events;
using SnagList.Domain.Staff;
using Xunit;

public class SnagNotificationDispatcherTests
{
    private static SnagNotificationDispatcher Build(FakeEmailSender emailSender, FakeStaffIdentityRepository staffIdentities) =>
        new(emailSender, staffIdentities, new NotificationOptions { MaintenanceTeamEmail = "maintenance@example.com" });

    [Fact]
    public async Task SnagReported_emails_the_maintenance_team_address()
    {
        var emailSender = new FakeEmailSender();
        var dispatcher = Build(emailSender, new FakeStaffIdentityRepository());

        await dispatcher.DispatchAsync(
            [new SnagReported(Guid.NewGuid(), Guid.NewGuid(), SnagSeverity.SafetyCritical, "U1", DateTimeOffset.UtcNow)], default);

        var sent = Assert.Single(emailSender.SentEmails);
        Assert.Equal("maintenance@example.com", sent.To);
    }

    [Fact]
    public async Task SnagStatusChanged_emails_the_reporter_when_their_identity_is_known()
    {
        var emailSender = new FakeEmailSender();
        var staffIdentities = new FakeStaffIdentityRepository();
        staffIdentities.Add(StaffIdentity.FirstSeen("U1", "Jane Smith", "jane@example.com", [StaffRole.Staff], DateTimeOffset.UtcNow));
        var dispatcher = Build(emailSender, staffIdentities);

        await dispatcher.DispatchAsync(
            [new SnagStatusChanged(Guid.NewGuid(), SnagStatus.Reported, SnagStatus.Acknowledged, "U9", "U1", DateTimeOffset.UtcNow)],
            default);

        var sent = Assert.Single(emailSender.SentEmails);
        Assert.Equal("jane@example.com", sent.To);
    }

    [Fact]
    public async Task SnagStatusChanged_sends_nothing_when_the_reporter_is_not_yet_mirrored()
    {
        var emailSender = new FakeEmailSender();
        var dispatcher = Build(emailSender, new FakeStaffIdentityRepository());

        await dispatcher.DispatchAsync(
            [new SnagStatusChanged(Guid.NewGuid(), SnagStatus.Reported, SnagStatus.Acknowledged, "U9", "U1", DateTimeOffset.UtcNow)],
            default);

        Assert.Empty(emailSender.SentEmails);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/SnagList.Application.Tests --filter SnagNotificationDispatcherTests`
Expected: FAIL — `SnagNotificationDispatcher`/`NotificationOptions` don't exist yet.

- [ ] **Step 3: Implement**

```csharp
namespace SnagList.Application.Notifications;

public sealed class NotificationOptions
{
    public string MaintenanceTeamEmail { get; set; } = "";
}
```

```csharp
namespace SnagList.Application.Notifications;

using SnagList.Application.Abstractions;
using SnagList.Domain.Snags.Events;

public sealed class SnagNotificationDispatcher(
    IEmailSender emailSender, IStaffIdentityRepository staffIdentities, NotificationOptions options)
{
    public async Task DispatchAsync(IReadOnlyList<object> domainEvents, CancellationToken ct)
    {
        foreach (var domainEvent in domainEvents)
        {
            switch (domainEvent)
            {
                case SnagReported reported:
                    await emailSender.SendAsync(
                        options.MaintenanceTeamEmail,
                        "New Snag reported",
                        $"A new {reported.Severity} severity Snag was reported (id: {reported.SnagId}).",
                        ct);
                    break;

                case SnagStatusChanged changed:
                    var reporter = await staffIdentities.GetAsync(changed.ReportedByStaffId, ct);
                    if (reporter is not null)
                    {
                        await emailSender.SendAsync(
                            reporter.Email,
                            $"Your Snag report changed status: {changed.NewStatus}",
                            $"Snag {changed.SnagId} moved from {changed.PreviousStatus} to {changed.NewStatus}.",
                            ct);
                    }
                    break;
            }
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/SnagList.Application.Tests --filter SnagNotificationDispatcherTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Wire dispatch into `EfUnitOfWork`**

Replace the whole class body written in Task 12 with:

```csharp
namespace SnagList.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using SnagList.Application.Abstractions;
using SnagList.Application.Notifications;
using SnagList.Application.Snags;
using SnagList.Domain.Snags;

public sealed class EfUnitOfWork(SnagListDbContext dbContext, SnagNotificationDispatcher notifications) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken ct)
    {
        var trackedSnags = dbContext.ChangeTracker.Entries<Snag>().Select(e => e.Entity).ToList();

        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            var entry = ex.Entries.Single();
            if (entry.Entity is not Snag snag) throw;

            var expectedVersion = (int)entry.OriginalValues["Version"]!;
            var databaseValues = await entry.GetDatabaseValuesAsync(ct);
            var actualVersion = databaseValues is null ? expectedVersion : (int)databaseValues["Version"]!;
            throw new SnagVersionConflictException(snag.Id, expectedVersion, actualVersion);
        }

        foreach (var snag in trackedSnags)
        {
            if (snag.DomainEvents.Count == 0) continue;
            await notifications.DispatchAsync(snag.DomainEvents, ct);
            snag.ClearDomainEvents();
        }
    }
}
```

The concurrency-conflict path still throws before reaching the dispatch loop, so a failed save
never sends a notification for a change that didn't actually persist.

- [ ] **Step 6: Update the two `EfUnitOfWorkTests` call sites (Task 12) for the new constructor parameter**

Replace:

```csharp
snagA.Edit("4th floor", SnagCategory.Electrical, SnagSeverity.Low, "edited by A");
await new EfUnitOfWork(contextA).SaveChangesAsync(default);

snagB.Edit("5th floor", SnagCategory.Electrical, SnagSeverity.Low, "edited by B, stale");
var ex = await Assert.ThrowsAsync<SnagVersionConflictException>(
    () => new EfUnitOfWork(contextB).SaveChangesAsync(default));
```

with:

```csharp
var noopDispatcher = new SnagNotificationDispatcher(
    new NoOpEmailSender(), new EfStaffIdentityRepository(contextA), new NotificationOptions());

snagA.Edit("4th floor", SnagCategory.Electrical, SnagSeverity.Low, "edited by A");
await new EfUnitOfWork(contextA, noopDispatcher).SaveChangesAsync(default);

snagB.Edit("5th floor", SnagCategory.Electrical, SnagSeverity.Low, "edited by B, stale");
var ex = await Assert.ThrowsAsync<SnagVersionConflictException>(
    () => new EfUnitOfWork(contextB, noopDispatcher).SaveChangesAsync(default));
```

This test is about the concurrency-conflict translation, not notifications, so it uses a trivial
`NoOpEmailSender` rather than pulling in Mailpit here too:

```csharp
namespace SnagList.Infrastructure.Tests.Testing;

using SnagList.Application.Abstractions;

public sealed class NoOpEmailSender : IEmailSender
{
    public Task SendAsync(string toAddress, string subject, string body, CancellationToken ct) => Task.CompletedTask;
}
```

- [ ] **Step 7: Run the full Infrastructure suite to verify nothing regressed**

Run: `dotnet test tests/SnagList.Infrastructure.Tests`
Expected: PASS.

- [ ] **Step 8: Wire `Program.cs`**

```csharp
builder.Services.Configure<NotificationOptions>(builder.Configuration.GetSection("Notifications"));
builder.Services.AddScoped(sp => sp.GetRequiredService<IOptions<NotificationOptions>>().Value);
builder.Services.AddScoped<SnagNotificationDispatcher>();
```

Add to `docker-compose.yml`'s `api` service environment (Task 22):

```yaml
      Notifications__MaintenanceTeamEmail: maintenance@example.com
```

- [ ] **Step 9: Run the full solution test suite, then commit**

Run: `dotnet test`
Expected: PASS — every project, Tasks 1–23.

```bash
git add src/SnagList.Application/Notifications src/SnagList.Infrastructure/Persistence/EfUnitOfWork.cs \
  tests/SnagList.Application.Tests/Testing/FakeEmailSender.cs tests/SnagList.Application.Tests/Notifications \
  tests/SnagList.Infrastructure.Tests/Persistence/EfUnitOfWorkTests.cs tests/SnagList.Infrastructure.Tests/Testing/NoOpEmailSender.cs \
  src/SnagList.Api/Program.cs docker-compose.yml
git commit -m "feat(notifications): dispatch SnagReported/SnagStatusChanged via email"
```

---

## Plan exit criteria

- `dotnet test` (every project) passes.
- `docker compose up -d --build && docker compose --profile seed run --rm seed` brings up a
  working stack; the Step 5 manual verification in Task 22 succeeds against it.
- Every REST resource and hypermedia transition from 03-api-design.md exists and is authorized per
  04-security-and-authentication.md.
- Reporting a `Snag` and changing its status send real emails, visible in Mailpit
  (http://localhost:8025), per 08-testing-and-nonfunctional.md.
- `docs/ontology.md`/`docs/ontology.ttl` already reflect this plan's domain model (committed
  alongside the design spec, before this plan existed) — no further ontology changes are expected
  from executing this plan, since it implements what was already modeled rather than introducing
  new domain concepts.

**Not in this plan** (separate follow-on plans, per the phased roadmap in
00-executive-summary.md): the MCP server, the Blazor WASM frontend, the home-lab deployment, and
the AWS deployment.
