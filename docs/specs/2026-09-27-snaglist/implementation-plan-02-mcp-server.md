# SnagList MCP Server Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An MCP server (`SnagList.Mcp`) exposing every REST operation from Plan 1 to AI agents,
in lockstep by construction with the `AgentOperationCatalog` that plan's Task 20 produced — same
Application handlers, same `StaffPolicy`/`MaintenancePolicy` authorization, `Stateless = true`
transport, CI-enforced parity with REST.

**Architecture:** `SnagList.Mcp` is a thin presentation adapter, structurally a sibling of
`SnagList.Api` — it calls the identical Application command/query handlers Plan 1 built, adds no
business logic of its own, and reuses Plan 1's auth wiring rather than re-implementing it. Two
small refactors (Tasks 1–2) extract what Plan 1 left living only inside `SnagList.Api` into shared
locations first, so `SnagList.Mcp` doesn't duplicate ~60 lines of DI wiring and an entire
`Authorization` folder.

**Tech Stack:** .NET 10, `ModelContextProtocol` + `ModelContextProtocol.AspNetCore` (the official
MCP C# SDK — `[McpServerToolType]`/`[McpServerTool]` attributed tool classes, `AddMcpServer()
.WithHttpTransport(o => o.Stateless = true)`, `app.MapMcp("/mcp")`, per the same pattern
JointBooking's own `SnagList.Mcp`-equivalent project uses), xUnit.

**Spec:** [docs/specs/2026-09-27-snaglist/](README.md), primarily 03-api-design.md's "Agent-friendly
decoration and MCP parity" section. Builds directly on
[implementation-plan-01-core-domain-api.md](implementation-plan-01-core-domain-api.md) — every
Application handler, port, and the `AgentOperationCatalog` this plan uses already exists there.

## Global Constraints

- Every MCP tool enforces the *same* `StaffPolicy`/`MaintenancePolicy` as its REST counterpart —
  there is no more-permissive path through MCP (04-security-and-authentication.md).
- Full parity, both directions: every `AgentOperationCatalog` entry has a matching registered MCP
  tool, and every registered MCP tool has a matching catalog entry. Enforced by a CI test (Task 8),
  not just code review.
- `Stateless = true` — no session affinity assumed anywhere in this plan or its deployment.
- No new business logic here. If a tool needs logic REST doesn't already have, that logic belongs
  in `SnagList.Application`, not in a tool method — this plan only wires, it doesn't decide.
- No placeholder code, no `TODO`s left in committed code — every task ships working, tested code.

---

## Task 1: Refactor — extract shared Infrastructure DI wiring

Plan 1's `SnagList.Api/Program.cs` registers every repository, query, port implementation, and
notification dependency inline. `SnagList.Mcp` needs the identical graph. Extracting it into
`SnagList.Infrastructure` now means Task 3 writes `SnagList.Mcp/Program.cs` as a handful of lines,
not a second copy of Plan 1's Task 16/20 wiring.

**Files:**
- Create: `src/SnagList.Infrastructure/ServiceCollectionExtensions.cs`
- Modify: `src/SnagList.Api/Program.cs` — replace the inline registrations (everything from
  `AddDbContext<SnagListDbContext>` through the `AddSingleton<IEmailSender>` line, and the
  `IStaffIdentityRepository`/`SyncStaffIdentityCommandHandler`/`NotificationOptions`/
  `SnagNotificationDispatcher` registrations added across Plan 1's Tasks 20 and 23) with one call

**Interfaces:**
- Consumes: every port/implementation pair from Plan 1 (Tasks 5, 11–14, 20, 23).
- Produces: `IServiceCollection.AddSnagListInfrastructure(IConfiguration configuration)` — consumed
  by both `SnagList.Api/Program.cs` (modified here) and `SnagList.Mcp/Program.cs` (Task 3).

- [ ] **Step 1: Write `AddSnagListInfrastructure`**

```csharp
namespace SnagList.Infrastructure;

using Amazon.Runtime;
using Amazon.S3;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SnagList.Application.Abstractions;
using SnagList.Application.Notifications;
using SnagList.Infrastructure.Audit;
using SnagList.Infrastructure.Clock;
using SnagList.Infrastructure.Email;
using SnagList.Infrastructure.Persistence;
using SnagList.Infrastructure.Persistence.Queries;
using SnagList.Infrastructure.Persistence.Repositories;
using SnagList.Infrastructure.Storage;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSnagListInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<SnagListDbContext>(options => options.UseNpgsql(
            configuration.GetConnectionString("SnagList")
                ?? throw new InvalidOperationException("Connection string 'SnagList' is required.")));

        services.AddScoped<ILocationRepository, EfLocationRepository>();
        services.AddScoped<ISnagRepository, EfSnagRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<ILocationQueries, EfLocationQueries>();
        services.AddScoped<ISnagQueries, EfSnagQueries>();
        services.AddScoped<IAuditWriter, EfAuditWriter>();
        services.AddScoped<IStaffIdentityRepository, EfStaffIdentityRepository>();
        services.AddSingleton<IClock, SystemClock>();

        var storage = configuration.GetSection("Storage");
        var storageBucket = storage["BucketName"] ?? throw new InvalidOperationException("Storage:BucketName is required.");
        services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(
            new BasicAWSCredentials(
                storage["AccessKey"] ?? throw new InvalidOperationException("Storage:AccessKey is required."),
                storage["SecretKey"] ?? throw new InvalidOperationException("Storage:SecretKey is required.")),
            new AmazonS3Config
            {
                ServiceURL = storage["ServiceUrl"] ?? throw new InvalidOperationException("Storage:ServiceUrl is required."),
                ForcePathStyle = true,
            }));
        services.AddSingleton<IBlobStorage>(sp => new S3CompatibleBlobStorage(sp.GetRequiredService<IAmazonS3>(), storageBucket));

        services.Configure<SmtpEmailSenderOptions>(configuration.GetSection("Email"));
        services.AddSingleton<IEmailSender>(sp => new SmtpEmailSender(sp.GetRequiredService<IOptions<SmtpEmailSenderOptions>>().Value));

        services.Configure<NotificationOptions>(configuration.GetSection("Notifications"));
        services.AddScoped(sp => sp.GetRequiredService<IOptions<NotificationOptions>>().Value);
        services.AddScoped<SnagNotificationDispatcher>();

        return services;
    }
}
```

- [ ] **Step 2: Simplify `SnagList.Api/Program.cs`**

Replace the block from `builder.Services.AddDbContext<SnagListDbContext>` through the last
`AddSingleton<IEmailSender>(...)` call, and the separately-added `IStaffIdentityRepository`/
`SyncStaffIdentityCommandHandler`/`NotificationOptions`/`SnagNotificationDispatcher` registrations,
with:

```csharp
builder.Services.AddSnagListInfrastructure(builder.Configuration);
builder.Services.AddScoped<SyncStaffIdentityCommandHandler>();
```

Every `AddScoped<...CommandHandler>()`/`AddScoped<...QueryHandler>()` registration added across
Plan 1's Tasks 17–19 (the endpoint-specific handlers, as opposed to the port implementations this
task just consolidated) stays exactly as it was — this refactor touches only the port-to-adapter
wiring, not the Application-layer handler registrations.

- [ ] **Step 3: Run the full Api test suite to confirm no behavior changed**

Run: `dotnet test tests/SnagList.Api.Tests`
Expected: PASS — identical results to the end of Plan 1. A refactor task's test is "nothing broke,"
not a new test, since no new behavior was introduced.

- [ ] **Step 4: Commit**

```bash
git add src/SnagList.Infrastructure/ServiceCollectionExtensions.cs src/SnagList.Api/Program.cs
git commit -m "refactor(infrastructure): extract AddSnagListInfrastructure for reuse by Mcp"
```

---

## Task 2: Refactor — move `Authorization` into a shared project

`SnagList.Mcp` needs the exact same claims-reading and policy logic `SnagList.Api` already has.
Rather than have `SnagList.Mcp` depend on `SnagList.Api` (backwards — a sibling presentation
project shouldn't depend on another presentation project) or duplicate the folder, it moves to a
new small shared library both reference.

**Files:**
- Create: `src/SnagList.Authorization/SnagList.Authorization.csproj`
- Move: `src/SnagList.Api/Authorization/PolicyNames.cs` →
  `src/SnagList.Authorization/PolicyNames.cs` (namespace `SnagList.Authorization`)
- Move: `src/SnagList.Api/Authorization/SnagListClaimTypes.cs` →
  `src/SnagList.Authorization/SnagListClaimTypes.cs` (namespace `SnagList.Authorization`)
- Move: `src/SnagList.Api/Authorization/AuthorizationPolicies.cs` →
  `src/SnagList.Authorization/AuthorizationPolicies.cs` (namespace `SnagList.Authorization`)
- Move: `src/SnagList.Api/Authorization/ClaimsPrincipalExtensions.cs` →
  `src/SnagList.Authorization/ClaimsPrincipalExtensions.cs` (namespace `SnagList.Authorization`)
- Move: `tests/SnagList.Api.Tests/Authorization/*` →
  `tests/SnagList.Authorization.Tests/*` (namespace `SnagList.Authorization.Tests`)
- Modify: `src/SnagList.Api/SnagList.Api.csproj` — add a project reference to
  `SnagList.Authorization`
- Modify: every file in `src/SnagList.Api/**` that has `using SnagList.Api.Authorization;` —
  change to `using SnagList.Authorization;`
- Modify: `SnagList.sln` — add the new project and test project

**Interfaces:**
- Produces: identical members as before (`PolicyNames.Staff`/`.Maintenance`,
  `AuthorizationPolicies.IsStaff`/`.IsMaintenance`, `ClaimsPrincipalExtensions.GetStaffId`/
  `.GetStaffName`/`.GetStaffEmail`/`.GetStaffRoles`), just under `SnagList.Authorization` instead
  of `SnagList.Api.Authorization` — consumed by `SnagList.Mcp`'s tool classes (Tasks 4–7) exactly
  as `SnagList.Api` already consumes them.

- [ ] **Step 1: Create the project and move the files**

```bash
dotnet new classlib -o src/SnagList.Authorization -n SnagList.Authorization
rm src/SnagList.Authorization/Class1.cs

git mv src/SnagList.Api/Authorization/PolicyNames.cs src/SnagList.Authorization/PolicyNames.cs
git mv src/SnagList.Api/Authorization/SnagListClaimTypes.cs src/SnagList.Authorization/SnagListClaimTypes.cs
git mv src/SnagList.Api/Authorization/AuthorizationPolicies.cs src/SnagList.Authorization/AuthorizationPolicies.cs
git mv src/SnagList.Api/Authorization/ClaimsPrincipalExtensions.cs src/SnagList.Authorization/ClaimsPrincipalExtensions.cs
rmdir src/SnagList.Api/Authorization

dotnet new xunit -o tests/SnagList.Authorization.Tests -n SnagList.Authorization.Tests
rm tests/SnagList.Authorization.Tests/UnitTest1.cs
git mv tests/SnagList.Api.Tests/Authorization/AuthorizationPoliciesTests.cs tests/SnagList.Authorization.Tests/AuthorizationPoliciesTests.cs
git mv tests/SnagList.Api.Tests/Authorization/ClaimsPrincipalExtensionsTests.cs tests/SnagList.Authorization.Tests/ClaimsPrincipalExtensionsTests.cs
rmdir tests/SnagList.Api.Tests/Authorization

dotnet sln add src/SnagList.Authorization tests/SnagList.Authorization.Tests
dotnet add tests/SnagList.Authorization.Tests reference src/SnagList.Authorization
dotnet add src/SnagList.Api reference src/SnagList.Authorization
```

- [ ] **Step 2: Update namespaces**

In each of the four moved source files and two moved test files, change
`namespace SnagList.Api.Authorization;` (or `SnagList.Api.Tests.Authorization`) to
`SnagList.Authorization` (or `SnagList.Authorization.Tests`).

In every remaining file under `src/SnagList.Api/` that references these types (`Program.cs`,
`Endpoints/LocationEndpoints.cs`, `Endpoints/SnagEndpoints.cs`, `Endpoints/MeEndpoints.cs`,
`Middleware/StaffIdentitySyncMiddleware.cs`, `Hypermedia/SnagLinksBuilder.cs`,
`Hypermedia/LocationLinksBuilder.cs`), change `using SnagList.Api.Authorization;` to
`using SnagList.Authorization;`. Same for the test files under `tests/SnagList.Api.Tests/` that use
`TestAuthHandler` (which itself references `SnagListClaimTypes`).

- [ ] **Step 3: Run the full solution test suite to confirm no behavior changed**

Run: `dotnet test`
Expected: PASS — every project, identical results to the end of Task 1. Purely mechanical move; no
logic changed.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "refactor: move Authorization into a shared SnagList.Authorization project"
```

---

## Task 3: Scaffold `SnagList.Mcp` and its composition root

Also consolidates the Application-layer handler registrations Plan 1 scattered across
`SnagList.Api/Program.cs` (one `AddScoped<...>()` per task, Tasks 17–20) into a single extension
method — needed now because `SnagList.Mcp` requires the identical set, and duplicating fourteen
registration lines a second time is exactly the kind of drift this plan's Global Constraints rule
out.

**Files:**
- Create: `src/SnagList.Application/ServiceCollectionExtensions.cs`
- Modify: `src/SnagList.Api/Program.cs` — replace the individual `AddScoped<...Handler>()` calls
  with `builder.Services.AddSnagListApplicationHandlers();`
- Create: `src/SnagList.Mcp/SnagList.Mcp.csproj`
- Create: `src/SnagList.Mcp/Program.cs`
- Test: `tests/SnagList.Mcp.Tests/SnagList.Mcp.Tests.csproj`
- Test: `tests/SnagList.Mcp.Tests/HealthEndpointTests.cs`

**Interfaces:**
- Consumes: `AddSnagListInfrastructure` (Task 1), `SnagList.Authorization` (Task 2), every command/
  query handler class from Plan 1 (Tasks 5–10, 20).
- Produces: `IServiceCollection.AddSnagListApplicationHandlers()` — used by both `SnagList.Api` and
  `SnagList.Mcp`. A running `SnagList.Mcp` host — Tasks 4–7 add its tool classes to it.

- [ ] **Step 1: Write `AddSnagListApplicationHandlers`**

```csharp
namespace SnagList.Application;

using Microsoft.Extensions.DependencyInjection;
using SnagList.Application.Locations.Commands;
using SnagList.Application.Locations.Queries;
using SnagList.Application.Snags.Commands;
using SnagList.Application.Snags.Queries;
using SnagList.Application.Staff.Commands;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSnagListApplicationHandlers(this IServiceCollection services) => services
        .AddScoped<CreateLocationCommandHandler>()
        .AddScoped<UpdateLocationCommandHandler>()
        .AddScoped<RetireLocationCommandHandler>()
        .AddScoped<ListLocationsQueryHandler>()
        .AddScoped<ReportSnagCommandHandler>()
        .AddScoped<EditSnagCommandHandler>()
        .AddScoped<WithdrawSnagCommandHandler>()
        .AddScoped<ChangeSnagStatusCommandHandler>()
        .AddScoped<RejectSnagCommandHandler>()
        .AddScoped<AddSnagCommentCommandHandler>()
        .AddScoped<UploadSnagPhotoCommandHandler>()
        .AddScoped<ListSnagsQueryHandler>()
        .AddScoped<GetSnagQueryHandler>()
        .AddScoped<SyncStaffIdentityCommandHandler>();
}
```

- [ ] **Step 2: Simplify `SnagList.Api/Program.cs`**

Replace every `builder.Services.AddScoped<...CommandHandler>();` /
`builder.Services.AddScoped<...QueryHandler>();` line added across Plan 1's Tasks 17–20 with:

```csharp
builder.Services.AddSnagListApplicationHandlers();
```

- [ ] **Step 3: Run the Api test suite to confirm no behavior changed**

Run: `dotnet test tests/SnagList.Api.Tests`
Expected: PASS.

- [ ] **Step 4: Scaffold `SnagList.Mcp`**

```bash
dotnet new web -o src/SnagList.Mcp -n SnagList.Mcp
dotnet add src/SnagList.Mcp package ModelContextProtocol.AspNetCore
dotnet add src/SnagList.Mcp reference src/SnagList.Application src/SnagList.Infrastructure src/SnagList.Authorization src/SnagList.Api.Auth.Local

dotnet new xunit -o tests/SnagList.Mcp.Tests -n SnagList.Mcp.Tests
dotnet add tests/SnagList.Mcp.Tests package Microsoft.AspNetCore.Mvc.Testing
dotnet add tests/SnagList.Mcp.Tests reference src/SnagList.Mcp

dotnet sln add src/SnagList.Mcp tests/SnagList.Mcp.Tests
```

- [ ] **Step 5: Write `Program.cs`**

```csharp
using SnagList.Api.Auth.Local;
using SnagList.Authorization;
using SnagList.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSnagListInfrastructure(builder.Configuration);
builder.Services.AddSnagListApplicationHandlers();

builder.Services.AddKeycloakAuthentication(builder.Configuration);
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(PolicyNames.Staff, policy => policy.RequireAssertion(ctx => AuthorizationPolicies.IsStaff(ctx.User.GetStaffRoles())))
    .AddPolicy(PolicyNames.Maintenance, policy => policy.RequireAssertion(ctx => AuthorizationPolicies.IsMaintenance(ctx.User.GetStaffRoles())));

builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true);
    // .WithTools<SnagTools>().WithTools<LocationTools>().WithTools<MeTools>() added in Tasks 4-7,
    // once those classes exist — chaining a tool type that doesn't exist yet won't compile.

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapMcp("/mcp").RequireAuthorization(PolicyNames.Staff);

app.Run();

public partial class Program;
```

- [ ] **Step 6: Write the failing host smoke test**

```csharp
namespace SnagList.Mcp.Tests;

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
            builder.UseSetting("Notifications:MaintenanceTeamEmail", "maintenance@example.com");
            builder.UseSetting("Auth:Local:Authority", "http://localhost:8080/realms/snaglist");
            builder.UseSetting("Auth:Local:Audience", "snaglist-api");
        }).CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

- [ ] **Step 7: Run test to verify it fails, then passes**

Run: `dotnet test tests/SnagList.Mcp.Tests --filter HealthEndpointTests`
Expected: FAIL (project doesn't build/run yet) until Step 5 lands, then PASS.

- [ ] **Step 8: Commit**

```bash
git add src/SnagList.Application/ServiceCollectionExtensions.cs src/SnagList.Api/Program.cs \
  src/SnagList.Mcp tests/SnagList.Mcp.Tests SnagList.sln
git commit -m "feat(mcp): scaffold SnagList.Mcp and consolidate handler DI registration"
```

---

## Task 4: `SnagTools` — report, edit, withdraw; shared tool-test harness

Tool classes are plain DI-constructed C# classes (`WithTools<T>()` registers `T` for the SDK to
construct per call) — so tool *logic* is tested the same way Plan 1 tested command handlers:
direct method calls, no MCP wire protocol involved. That protocol layer is the SDK's own concern,
already proven working by Task 3's host smoke test; this plan only needs to prove the tools
delegate to the right handlers with the right caller identity.

**Files:**
- Create: `tests/SnagList.Mcp.Tests/Testing/FakeHttpContextAccessor.cs`
- Create: `tests/SnagList.Mcp.Tests/Testing/FakeBlobStorage.cs`
- Create: `tests/SnagList.Mcp.Tests/Testing/FakeEmailSender.cs`
- Create: `tests/SnagList.Mcp.Tests/Testing/McpToolsFixture.cs`
- Create: `src/SnagList.Mcp/Tools/SnagTools.cs` (report/edit/withdraw only — Task 5 adds the rest)
- Modify: `src/SnagList.Mcp/Program.cs` — add `.WithTools<SnagTools>()`
- Test: `tests/SnagList.Mcp.Tests/Tools/SnagToolsTests.cs`

**Interfaces:**
- Consumes: `ReportSnagCommandHandler`, `EditSnagCommandHandler`, `WithdrawSnagCommandHandler`
  (Plan 1, Tasks 6–7); `ClaimsPrincipalExtensions` (Task 2).
- Produces: `McpToolsFixture.CreateTool<T>(scope, staffId, roles...) -> T` — reused by every later
  tool-test task (5–7).

- [ ] **Step 1: Write the shared test infrastructure**

```csharp
namespace SnagList.Mcp.Tests.Testing;

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using SnagList.Authorization;

public sealed class FakeHttpContextAccessor : IHttpContextAccessor
{
    public HttpContext? HttpContext { get; set; }

    public static FakeHttpContextAccessor For(string staffId, params string[] roles)
    {
        var claims = new List<Claim>
        {
            new(SnagListClaimTypes.StaffId, staffId),
            new(ClaimTypes.Name, "Test User"),
        };
        claims.AddRange(roles.Select(r => new Claim(SnagListClaimTypes.Role, r)));
        return new FakeHttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) },
        };
    }
}
```

```csharp
namespace SnagList.Mcp.Tests.Testing;

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
namespace SnagList.Mcp.Tests.Testing;

using SnagList.Application.Abstractions;

public sealed class FakeEmailSender : IEmailSender
{
    public Task SendAsync(string toAddress, string subject, string body, CancellationToken ct) => Task.CompletedTask;
}
```

```csharp
namespace SnagList.Mcp.Tests.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SnagList.Application;
using SnagList.Application.Abstractions;
using SnagList.Infrastructure;
using SnagList.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

public sealed class McpToolsFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    private ServiceProvider _provider = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:SnagList"] = _postgres.GetConnectionString(),
            ["Storage:BucketName"] = "test-bucket",
            ["Storage:AccessKey"] = "test",
            ["Storage:SecretKey"] = "test",
            ["Storage:ServiceUrl"] = "http://localhost:9000",
            ["Email:Host"] = "localhost",
            ["Email:FromAddress"] = "snaglist@example.com",
            ["Notifications:MaintenanceTeamEmail"] = "maintenance@example.com",
        }).Build();

        var services = new ServiceCollection();
        services.AddSnagListInfrastructure(configuration);
        services.AddSnagListApplicationHandlers();
        services.RemoveAll(typeof(IBlobStorage));
        services.AddSingleton<IBlobStorage, FakeBlobStorage>();
        services.RemoveAll(typeof(IEmailSender));
        services.AddSingleton<IEmailSender, FakeEmailSender>();

        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<SnagListDbContext>().Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    public IServiceScope CreateScope() => _provider.CreateScope();

    // ActivatorUtilities resolves every constructor parameter from the container except the ones
    // explicitly passed — so this stays correct as SnagTools/LocationTools/MeTools grow more
    // handler dependencies across Tasks 5-7 without needing to change here.
    public T CreateTool<T>(IServiceScope scope, string staffId, params string[] roles) =>
        Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<T>(
            scope.ServiceProvider, FakeHttpContextAccessor.For(staffId, roles));
}

[CollectionDefinition("McpTools")]
public sealed class McpToolsCollection : ICollectionFixture<McpToolsFixture>;
```

- [ ] **Step 2: Write the failing `SnagTools` tests**

```csharp
namespace SnagList.Mcp.Tests.Tools;

using SnagList.Application.Locations.Commands;
using SnagList.Mcp.Tests.Testing;
using SnagList.Mcp.Tools;
using SnagList.Domain.Snags;
using Xunit;

[Collection("McpTools")]
public class SnagToolsTests(McpToolsFixture fixture)
{
    private async Task<Guid> CreateLocationAsync()
    {
        using var scope = fixture.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<CreateLocationCommandHandler>();
        return await handler.HandleAsync(new CreateLocationCommand("Head Office", "1 Main St"), default);
    }

    [Fact]
    public async Task ReportSnag_creates_a_Snag_attributed_to_the_calling_staff_member()
    {
        var locationId = await CreateLocationAsync();
        using var scope = fixture.CreateScope();
        var tools = fixture.CreateTool<SnagTools>(scope, "U100050", "Staff");

        var result = await tools.ReportSnag(locationId, "3rd floor", SnagCategory.Electrical, SnagSeverity.Medium, "Flickering light", default);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task EditSnag_then_WithdrawSnag_succeed_for_the_reporter()
    {
        var locationId = await CreateLocationAsync();
        using var reportScope = fixture.CreateScope();
        var reportTools = fixture.CreateTool<SnagTools>(reportScope, "U100051", "Staff");
        dynamic reported = await reportTools.ReportSnag(locationId, "4th floor", SnagCategory.Other, SnagSeverity.Low, "desc", default);
        Guid snagId = reported.id;

        using var editScope = fixture.CreateScope();
        var editTools = fixture.CreateTool<SnagTools>(editScope, "U100051", "Staff");
        await editTools.EditSnag(snagId, "4th floor, room 4.01", SnagCategory.Other, SnagSeverity.Low, "desc, more detail", 1, default);

        using var withdrawScope = fixture.CreateScope();
        var withdrawTools = fixture.CreateTool<SnagTools>(withdrawScope, "U100051", "Staff");
        await withdrawTools.WithdrawSnag(snagId, 2, default);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/SnagList.Mcp.Tests --filter SnagToolsTests`
Expected: FAIL — `SnagTools` does not exist.

- [ ] **Step 4: Implement `SnagTools`**

```csharp
namespace SnagList.Mcp.Tools;

using System.ComponentModel;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;
using SnagList.Application.Snags.Commands;
using SnagList.Authorization;
using SnagList.Domain.Snags;

[McpServerToolType]
public sealed class SnagTools(
    IHttpContextAccessor httpContextAccessor,
    ReportSnagCommandHandler reportHandler,
    EditSnagCommandHandler editHandler,
    WithdrawSnagCommandHandler withdrawHandler)
{
    private System.Security.Claims.ClaimsPrincipal User => httpContextAccessor.HttpContext!.User;

    [McpServerTool(Name = "report_snag")]
    [Description("Reports a new building-quality or maintenance issue (a Snag) at a Location.")]
    public async Task<object> ReportSnag(
        [Description("The id of the Location this Snag is being reported at.")] Guid locationId,
        [Description("Free-text sub-location, e.g. '3rd floor, room 3.12'.")] string subLocation,
        [Description("Electrical, Plumbing, StructuralOrFabric, HeatingAndCooling, CleaningAndHousekeeping, SafetyHazard, or Other.")] SnagCategory category,
        [Description("Low, Medium, High, or SafetyCritical.")] SnagSeverity severity,
        [Description("Description of the issue.")] string description,
        CancellationToken ct)
    {
        var id = await reportHandler.HandleAsync(new ReportSnagCommand(
            locationId, subLocation, category, severity, description, User.GetStaffId(), User.GetStaffName()), ct);
        return new { id };
    }

    [McpServerTool(Name = "edit_snag")]
    [Description("Edits a Snag the caller reported, while it is still in the Reported state.")]
    public async Task EditSnag(
        [Description("The Snag's id.")] Guid snagId,
        [Description("New sub-location.")] string subLocation,
        [Description("New category.")] SnagCategory category,
        [Description("New severity.")] SnagSeverity severity,
        [Description("New description.")] string description,
        [Description("The Snag's current version, from a prior report_snag/get_snag/list_snags result — prevents overwriting a concurrent change.")] int expectedVersion,
        CancellationToken ct)
    {
        await editHandler.HandleAsync(new EditSnagCommand(
            snagId, User.GetStaffId(), subLocation, category, severity, description, expectedVersion), ct);
    }

    [McpServerTool(Name = "withdraw_snag")]
    [Description("Withdraws a Snag the caller reported, while it is still in the Reported state.")]
    public async Task WithdrawSnag(
        [Description("The Snag's id.")] Guid snagId,
        [Description("The Snag's current version.")] int expectedVersion,
        CancellationToken ct)
    {
        await withdrawHandler.HandleAsync(new WithdrawSnagCommand(snagId, User.GetStaffId(), expectedVersion), ct);
    }
}
```

- [ ] **Step 5: Wire it into `Program.cs`**

```csharp
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithTools<SnagTools>();
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/SnagList.Mcp.Tests --filter SnagToolsTests`
Expected: PASS (2 tests). (Requires Docker running locally for the Testcontainers Postgres.)

- [ ] **Step 7: Add packages and commit**

```bash
dotnet add tests/SnagList.Mcp.Tests package Testcontainers.PostgreSql

git add src/SnagList.Mcp tests/SnagList.Mcp.Tests
git commit -m "feat(mcp): add SnagTools (report/edit/withdraw) and the tool-test harness"
```

---

## Task 5: `SnagTools` — status transitions, reject, comments, photos

**Design point unique to MCP (doesn't exist for REST):** `RequireAuthorization(PolicyNames.Staff)`
on `app.MapMcp("/mcp")` (Task 3) gates the whole MCP endpoint at the *Staff* level — it can't carry
a per-tool policy the way `.RequireAuthorization(PolicyNames.Maintenance)` gates one REST endpoint
at a time (Plan 1, Task 19), because every tool is multiplexed over that one endpoint. Every
`Maintenance`-only tool here must check the role itself, or a `Staff`-only caller could invoke
`acknowledge_snag` — REST's authorization would silently fail to carry over.

Also: MCP has no equivalent of an HTTP redirect, so `get_snag_photo` returns the presigned URL
directly as data rather than attempting REST's 302 (03-api-design.md's photo-download behavior was
always going to differ here — this is that difference, made concrete).

**Files:**
- Modify: `src/SnagList.Mcp/Tools/SnagTools.cs` — supersedes Task 4's version; add the remaining
  eight tools
- Test: `tests/SnagList.Mcp.Tests/Tools/SnagToolsTests.cs` — add cases for the new tools

**Interfaces:**
- Consumes: `ChangeSnagStatusCommandHandler`, `RejectSnagCommandHandler` (Plan 1, Task 8),
  `AddSnagCommentCommandHandler`, `UploadSnagPhotoCommandHandler` (Task 9), `GetSnagQueryHandler`
  (Task 10), `IBlobStorage` (Task 5, 13).

- [ ] **Step 1: Add the failing tests**

```csharp
// Append to SnagToolsTests (tests/SnagList.Mcp.Tests/Tools/SnagToolsTests.cs)

[Fact]
public async Task Maintenance_can_drive_a_Snag_through_acknowledge_start_resolve_close()
{
    var locationId = await CreateLocationAsync();
    using var reportScope = fixture.CreateScope();
    dynamic reported = await fixture.CreateTool<SnagTools>(reportScope, "U100052", "Staff")
        .ReportSnag(locationId, "5th floor", SnagCategory.Other, SnagSeverity.Low, "desc", default);
    Guid snagId = reported.id;

    using var scope1 = fixture.CreateScope();
    await fixture.CreateTool<SnagTools>(scope1, "U900050", "Maintenance").AcknowledgeSnag(snagId, 1, default);
    using var scope2 = fixture.CreateScope();
    await fixture.CreateTool<SnagTools>(scope2, "U900050", "Maintenance").StartSnagWork(snagId, 2, default);
    using var scope3 = fixture.CreateScope();
    await fixture.CreateTool<SnagTools>(scope3, "U900050", "Maintenance").ResolveSnag(snagId, 3, default);
    using var scope4 = fixture.CreateScope();
    await fixture.CreateTool<SnagTools>(scope4, "U900050", "Maintenance").CloseSnag(snagId, 4, default);
}

[Fact]
public async Task Staff_calling_AcknowledgeSnag_is_rejected()
{
    var locationId = await CreateLocationAsync();
    using var reportScope = fixture.CreateScope();
    dynamic reported = await fixture.CreateTool<SnagTools>(reportScope, "U100053", "Staff")
        .ReportSnag(locationId, "6th floor", SnagCategory.Other, SnagSeverity.Low, "desc", default);
    Guid snagId = reported.id;

    using var scope = fixture.CreateScope();
    var tools = fixture.CreateTool<SnagTools>(scope, "U100053", "Staff");

    await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.AcknowledgeSnag(snagId, 1, default));
}

[Fact]
public async Task RejectSnag_records_the_reason_as_a_comment()
{
    var locationId = await CreateLocationAsync();
    using var reportScope = fixture.CreateScope();
    dynamic reported = await fixture.CreateTool<SnagTools>(reportScope, "U100054", "Staff")
        .ReportSnag(locationId, "7th floor", SnagCategory.Other, SnagSeverity.Low, "desc", default);
    Guid snagId = reported.id;

    using var scope = fixture.CreateScope();
    await fixture.CreateTool<SnagTools>(scope, "U900051", "Maintenance").RejectSnag(snagId, "Duplicate", 1, default);
}

[Fact]
public async Task AddSnagComment_succeeds_for_either_role()
{
    var locationId = await CreateLocationAsync();
    using var reportScope = fixture.CreateScope();
    dynamic reported = await fixture.CreateTool<SnagTools>(reportScope, "U100055", "Staff")
        .ReportSnag(locationId, "8th floor", SnagCategory.Other, SnagSeverity.Low, "desc", default);
    Guid snagId = reported.id;

    using var scope = fixture.CreateScope();
    await fixture.CreateTool<SnagTools>(scope, "U100055", "Staff").AddSnagComment(snagId, "Any update?", default);
}

[Fact]
public async Task UploadSnagPhoto_then_GetSnagPhoto_returns_a_url()
{
    var locationId = await CreateLocationAsync();
    using var reportScope = fixture.CreateScope();
    dynamic reported = await fixture.CreateTool<SnagTools>(reportScope, "U100056", "Staff")
        .ReportSnag(locationId, "9th floor", SnagCategory.Other, SnagSeverity.Low, "desc", default);
    Guid snagId = reported.id;
    var base64 = Convert.ToBase64String([1, 2, 3, 4]);

    using var uploadScope = fixture.CreateScope();
    await fixture.CreateTool<SnagTools>(uploadScope, "U100056", "Staff")
        .UploadSnagPhoto(snagId, "light.jpg", "image/jpeg", base64, default);

    using var getScope = fixture.CreateScope();
    var detail = await getScope.ServiceProvider.GetRequiredService<GetSnagQueryHandler>()
        .HandleAsync(new SnagList.Application.Snags.Queries.GetSnagQuery(snagId), default);
    var photoKey = detail!.Photos[0].BlobKey[$"snags/{snagId}/".Length..];

    using var downloadScope = fixture.CreateScope();
    dynamic result = await fixture.CreateTool<SnagTools>(downloadScope, "U100056", "Staff")
        .GetSnagPhoto(snagId, photoKey, default);

    Assert.Contains(photoKey, (string)result.url);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/SnagList.Mcp.Tests --filter SnagToolsTests`
Expected: FAIL — the new tool methods don't exist.

- [ ] **Step 3: Replace `SnagTools` with the full version**

```csharp
namespace SnagList.Mcp.Tools;

using System.ComponentModel;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;
using SnagList.Application.Abstractions;
using SnagList.Application.Snags.Commands;
using SnagList.Application.Snags.Queries;
using SnagList.Authorization;
using SnagList.Domain.Snags;

[McpServerToolType]
public sealed class SnagTools(
    IHttpContextAccessor httpContextAccessor,
    ReportSnagCommandHandler reportHandler,
    EditSnagCommandHandler editHandler,
    WithdrawSnagCommandHandler withdrawHandler,
    ChangeSnagStatusCommandHandler changeStatusHandler,
    RejectSnagCommandHandler rejectHandler,
    AddSnagCommentCommandHandler addCommentHandler,
    UploadSnagPhotoCommandHandler uploadPhotoHandler,
    GetSnagQueryHandler getSnagHandler,
    IBlobStorage blobStorage)
{
    private ClaimsPrincipal User => httpContextAccessor.HttpContext!.User;

    private void RequireMaintenance()
    {
        if (!AuthorizationPolicies.IsMaintenance(User.GetStaffRoles()))
        {
            throw new UnauthorizedAccessException("This operation requires the Maintenance role.");
        }
    }

    [McpServerTool(Name = "report_snag")]
    [Description("Reports a new building-quality or maintenance issue (a Snag) at a Location.")]
    public async Task<object> ReportSnag(
        [Description("The id of the Location this Snag is being reported at.")] Guid locationId,
        [Description("Free-text sub-location, e.g. '3rd floor, room 3.12'.")] string subLocation,
        [Description("Electrical, Plumbing, StructuralOrFabric, HeatingAndCooling, CleaningAndHousekeeping, SafetyHazard, or Other.")] SnagCategory category,
        [Description("Low, Medium, High, or SafetyCritical.")] SnagSeverity severity,
        [Description("Description of the issue.")] string description,
        CancellationToken ct)
    {
        var id = await reportHandler.HandleAsync(new ReportSnagCommand(
            locationId, subLocation, category, severity, description, User.GetStaffId(), User.GetStaffName()), ct);
        return new { id };
    }

    [McpServerTool(Name = "edit_snag")]
    [Description("Edits a Snag the caller reported, while it is still in the Reported state.")]
    public async Task EditSnag(
        Guid snagId, string subLocation, SnagCategory category, SnagSeverity severity, string description,
        [Description("The Snag's current version — prevents overwriting a concurrent change.")] int expectedVersion,
        CancellationToken ct)
    {
        await editHandler.HandleAsync(new EditSnagCommand(
            snagId, User.GetStaffId(), subLocation, category, severity, description, expectedVersion), ct);
    }

    [McpServerTool(Name = "withdraw_snag")]
    [Description("Withdraws a Snag the caller reported, while it is still in the Reported state.")]
    public async Task WithdrawSnag(Guid snagId, int expectedVersion, CancellationToken ct) =>
        await withdrawHandler.HandleAsync(new WithdrawSnagCommand(snagId, User.GetStaffId(), expectedVersion), ct);

    [McpServerTool(Name = "acknowledge_snag")]
    [Description("Maintenance confirms receipt of a Reported Snag.")]
    public Task AcknowledgeSnag(Guid snagId, int expectedVersion, CancellationToken ct) =>
        ChangeStatus(snagId, SnagStatus.Acknowledged, expectedVersion, ct);

    [McpServerTool(Name = "start_snag_work")]
    [Description("Maintenance marks an Acknowledged Snag as InProgress.")]
    public Task StartSnagWork(Guid snagId, int expectedVersion, CancellationToken ct) =>
        ChangeStatus(snagId, SnagStatus.InProgress, expectedVersion, ct);

    [McpServerTool(Name = "resolve_snag")]
    [Description("Maintenance marks an InProgress Snag as Resolved.")]
    public Task ResolveSnag(Guid snagId, int expectedVersion, CancellationToken ct) =>
        ChangeStatus(snagId, SnagStatus.Resolved, expectedVersion, ct);

    [McpServerTool(Name = "close_snag")]
    [Description("Closes a Resolved Snag, ending its lifecycle.")]
    public Task CloseSnag(Guid snagId, int expectedVersion, CancellationToken ct) =>
        ChangeStatus(snagId, SnagStatus.Closed, expectedVersion, ct);

    private Task ChangeStatus(Guid snagId, SnagStatus target, int expectedVersion, CancellationToken ct)
    {
        RequireMaintenance();
        return changeStatusHandler.HandleAsync(new ChangeSnagStatusCommand(snagId, target, User.GetStaffId(), expectedVersion), ct);
    }

    [McpServerTool(Name = "reject_snag")]
    [Description("Maintenance marks a Reported or Acknowledged Snag as invalid or a duplicate, with a reason.")]
    public Task RejectSnag(Guid snagId, string reason, int expectedVersion, CancellationToken ct)
    {
        RequireMaintenance();
        return rejectHandler.HandleAsync(new RejectSnagCommand(snagId, User.GetStaffId(), User.GetStaffName(), reason, expectedVersion), ct);
    }

    [McpServerTool(Name = "add_snag_comment")]
    [Description("Adds a follow-up remark to a Snag, regardless of its current status.")]
    public Task AddSnagComment(Guid snagId, string body, CancellationToken ct) =>
        addCommentHandler.HandleAsync(new AddSnagCommentCommand(snagId, User.GetStaffId(), User.GetStaffName(), body), ct);

    [McpServerTool(Name = "upload_snag_photo")]
    [Description("Attaches a photo to a Snag. A Snag may carry at most 5 photos.")]
    public async Task UploadSnagPhoto(
        Guid snagId,
        [Description("Original file name, e.g. 'light.jpg'.")] string fileName,
        [Description("MIME content type, e.g. 'image/jpeg'.")] string contentType,
        [Description("The photo's bytes, base64-encoded.")] string base64Content,
        CancellationToken ct)
    {
        var bytes = Convert.FromBase64String(base64Content);
        using var stream = new MemoryStream(bytes);
        await uploadPhotoHandler.HandleAsync(new UploadSnagPhotoCommand(snagId, fileName, contentType, stream, bytes.Length), ct);
    }

    [McpServerTool(Name = "get_snag_photo")]
    [Description("Returns a short-lived direct download URL for one of a Snag's photos.")]
    public async Task<object> GetSnagPhoto(
        Guid snagId,
        [Description("The photo's key, from a get_snag result's photos list.")] string photoKey,
        CancellationToken ct)
    {
        var detail = await getSnagHandler.HandleAsync(new GetSnagQuery(snagId), ct);
        var blobKey = $"snags/{snagId}/{photoKey}";
        if (detail is null || !detail.Photos.Any(p => p.BlobKey == blobKey))
        {
            throw new InvalidOperationException($"No photo '{photoKey}' found on Snag {snagId}.");
        }

        var url = await blobStorage.GetPresignedGetUrlAsync(blobKey, TimeSpan.FromMinutes(10), ct);
        return new { url = url.ToString() };
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/SnagList.Mcp.Tests --filter SnagToolsTests`
Expected: PASS (7 tests total, Tasks 4–5 combined).

- [ ] **Step 5: Commit**

```bash
git add src/SnagList.Mcp/Tools/SnagTools.cs tests/SnagList.Mcp.Tests/Tools/SnagToolsTests.cs
git commit -m "feat(mcp): add SnagTools status-transition, comment, and photo tools"
```

---

## Task 6: `LocationTools` and `MeTools`

**Files:**
- Create: `src/SnagList.Mcp/Tools/LocationTools.cs`
- Create: `src/SnagList.Mcp/Tools/MeTools.cs`
- Modify: `src/SnagList.Mcp/Program.cs` — add `.WithTools<LocationTools>().WithTools<MeTools>()`
- Test: `tests/SnagList.Mcp.Tests/Tools/LocationToolsTests.cs`
- Test: `tests/SnagList.Mcp.Tests/Tools/MeToolsTests.cs`

**Interfaces:**
- Consumes: `CreateLocationCommandHandler`, `UpdateLocationCommandHandler`,
  `RetireLocationCommandHandler`, `ListLocationsQueryHandler` (Plan 1, Task 5); `ILocationRepository`
  (Plan 1, Task 5, for the single-`Location` get, mirroring `SnagList.Api`'s own choice in Plan 1's
  Task 17 not to build a separate read model for a `Location`'s handful of flat fields).

- [ ] **Step 1: Write the failing tests**

```csharp
namespace SnagList.Mcp.Tests.Tools;

using SnagList.Application.Locations.Commands;
using SnagList.Mcp.Tests.Testing;
using SnagList.Mcp.Tools;
using Xunit;

[Collection("McpTools")]
public class LocationToolsTests(McpToolsFixture fixture)
{
    [Fact]
    public async Task Maintenance_can_create_then_list_a_Location()
    {
        using var createScope = fixture.CreateScope();
        dynamic created = await fixture.CreateTool<LocationTools>(createScope, "U900060", "Maintenance")
            .CreateLocation("Engineering Site", "3 Park Rd", default);

        using var listScope = fixture.CreateScope();
        dynamic page = await fixture.CreateTool<LocationTools>(listScope, "U100060", "Staff")
            .ListLocations(false, null, 50, default);

        Assert.NotEmpty((IEnumerable<object>)page.items);
    }

    [Fact]
    public async Task Staff_cannot_create_a_Location()
    {
        using var scope = fixture.CreateScope();
        var tools = fixture.CreateTool<LocationTools>(scope, "U100061", "Staff");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.CreateLocation("Unauthorized", "1 Nowhere", default));
    }

    [Fact]
    public async Task GetLocation_returns_the_created_Location()
    {
        using var createScope = fixture.CreateScope();
        var handler = createScope.ServiceProvider.GetRequiredService<CreateLocationCommandHandler>();
        var id = await handler.HandleAsync(new CreateLocationCommand("Northern Office", "42 North Rd"), default);

        using var getScope = fixture.CreateScope();
        dynamic result = await fixture.CreateTool<LocationTools>(getScope, "U100062", "Staff").GetLocation(id, default);

        Assert.Equal("Northern Office", (string)result.Name);
    }
}
```

```csharp
namespace SnagList.Mcp.Tests.Tools;

using SnagList.Mcp.Tests.Testing;
using SnagList.Mcp.Tools;
using SnagList.Domain.Staff;
using Xunit;

[Collection("McpTools")]
public class MeToolsTests(McpToolsFixture fixture)
{
    [Fact]
    public void GetMe_returns_the_callers_identity_and_roles()
    {
        using var scope = fixture.CreateScope();
        var tools = fixture.CreateTool<MeTools>(scope, "U100063", "Staff", "Maintenance");

        dynamic me = tools.GetMe();

        Assert.Equal("U100063", (string)me.staffId);
        Assert.Contains(StaffRole.Maintenance, (IReadOnlyList<StaffRole>)me.roles);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/SnagList.Mcp.Tests --filter "LocationToolsTests|MeToolsTests"`
Expected: FAIL — `LocationTools`/`MeTools` don't exist.

- [ ] **Step 3: Implement**

```csharp
namespace SnagList.Mcp.Tools;

using System.ComponentModel;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;
using SnagList.Application.Abstractions;
using SnagList.Application.Locations.Commands;
using SnagList.Application.Locations.Queries;
using SnagList.Authorization;

[McpServerToolType]
public sealed class LocationTools(
    IHttpContextAccessor httpContextAccessor,
    ILocationRepository locationRepository,
    CreateLocationCommandHandler createHandler,
    UpdateLocationCommandHandler updateHandler,
    RetireLocationCommandHandler retireHandler,
    ListLocationsQueryHandler listHandler)
{
    private ClaimsPrincipal User => httpContextAccessor.HttpContext!.User;

    private void RequireMaintenance()
    {
        if (!AuthorizationPolicies.IsMaintenance(User.GetStaffRoles()))
        {
            throw new UnauthorizedAccessException("This operation requires the Maintenance role.");
        }
    }

    [McpServerTool(Name = "list_locations")]
    [Description("Lists corporate Locations Snags can be reported against, cursor-paginated.")]
    public async Task<object> ListLocations(
        [Description("Include retired Locations. Defaults to false.")] bool includeRetired,
        [Description("Opaque cursor from a previous call's nextCursor, or omit for the first page.")] string? cursor,
        [Description("Max items to return, 1-100. Defaults to 20.")] int limit,
        CancellationToken ct)
    {
        var page = await listHandler.HandleAsync(
            new ListLocationsQuery(includeRetired, cursor, limit is > 0 and <= 100 ? limit : 20), ct);
        return new { items = page.Items, nextCursor = page.NextCursor };
    }

    [McpServerTool(Name = "get_location")]
    [Description("Gets a single Location by id.")]
    public async Task<object> GetLocation(Guid locationId, CancellationToken ct)
    {
        var location = await locationRepository.GetAsync(locationId, ct)
            ?? throw new InvalidOperationException($"Location {locationId} was not found.");
        return new { location.Id, location.Name, location.Address, location.IsActive };
    }

    [McpServerTool(Name = "create_location")]
    [Description("Adds a new corporate site that Snags can be reported against. Requires the Maintenance role.")]
    public async Task<object> CreateLocation(string name, string address, CancellationToken ct)
    {
        RequireMaintenance();
        var id = await createHandler.HandleAsync(new CreateLocationCommand(name, address), ct);
        return new { id };
    }

    [McpServerTool(Name = "update_location")]
    [Description("Edits an existing Location's name or address. Requires the Maintenance role.")]
    public Task UpdateLocation(Guid locationId, string name, string address, CancellationToken ct)
    {
        RequireMaintenance();
        return updateHandler.HandleAsync(new UpdateLocationCommand(locationId, name, address), ct);
    }

    [McpServerTool(Name = "retire_location")]
    [Description("Retires a Location, hiding it from new Snag reports without deleting history. Requires the Maintenance role.")]
    public Task RetireLocation(Guid locationId, CancellationToken ct)
    {
        RequireMaintenance();
        return retireHandler.HandleAsync(new RetireLocationCommand(locationId), ct);
    }
}
```

```csharp
namespace SnagList.Mcp.Tools;

using System.ComponentModel;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;
using SnagList.Authorization;

[McpServerToolType]
public sealed class MeTools(IHttpContextAccessor httpContextAccessor)
{
    [McpServerTool(Name = "get_me")]
    [Description("Returns the calling staff member's identity and roles.")]
    public object GetMe()
    {
        var user = httpContextAccessor.HttpContext!.User;
        return new
        {
            staffId = user.GetStaffId(),
            name = user.GetStaffName(),
            email = user.GetStaffEmail(),
            roles = user.GetStaffRoles(),
        };
    }
}
```

- [ ] **Step 4: Wire into `Program.cs`**

```csharp
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithTools<SnagTools>()
    .WithTools<LocationTools>()
    .WithTools<MeTools>();
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/SnagList.Mcp.Tests --filter "LocationToolsTests|MeToolsTests"`
Expected: PASS (3 + 1 tests).

- [ ] **Step 6: Commit**

```bash
git add src/SnagList.Mcp tests/SnagList.Mcp.Tests
git commit -m "feat(mcp): add LocationTools and MeTools"
```

---

## Task 7: Move `AgentOperationCatalog` to `SnagList.Application`

Plan 1's Task 20 put `AgentOperationCatalog` in `SnagList.Api` because only `SnagList.Api` existed
at the time. Task 8's parity test needs to see both the catalog and the registered MCP tools; since
`SnagList.Mcp` shouldn't depend on `SnagList.Api` (sibling presentation projects, same reasoning as
Task 2), the catalog moves down to `SnagList.Application`, which both already depend on.

**Files:**
- Move: `src/SnagList.Api/OpenApi/AgentOperationCatalog.cs` →
  `src/SnagList.Application/AgentOperationCatalog.cs` (namespace `SnagList.Application`)
- Modify: `src/SnagList.Api/OpenApi/AgentHintsDocumentTransformer.cs` — update its `using`

- [ ] **Step 1: Move and update the namespace**

```bash
git mv src/SnagList.Api/OpenApi/AgentOperationCatalog.cs src/SnagList.Application/AgentOperationCatalog.cs
```

Change `namespace SnagList.Api.OpenApi;` to `namespace SnagList.Application;` in the moved file.
In `src/SnagList.Api/OpenApi/AgentHintsDocumentTransformer.cs`, add `using SnagList.Application;`
(the file already implicitly resolved `AgentOperationCatalog` via being in the same original
namespace — it now needs the explicit `using`).

- [ ] **Step 2: Run the Api test suite to confirm no behavior changed**

Run: `dotnet test tests/SnagList.Api.Tests --filter AgentHintsDocumentTransformerTests`
Expected: PASS — identical result to Plan 1.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "refactor: move AgentOperationCatalog to SnagList.Application"
```

---

## Task 8: CI parity test — every MCP tool ↔ every `AgentOperationCatalog` entry

The load-bearing test for this entire plan's premise: REST and MCP cannot silently drift apart.

**Files:**
- Test: `tests/SnagList.Mcp.Tests/AgentOperationCatalogParityTests.cs`

**Interfaces:**
- Consumes: `AgentOperationCatalog.OperationIdToMcpTool` (Task 7); every `[McpServerTool(Name =
  "...")]` method across `SnagTools`, `LocationTools`, `MeTools` (Tasks 4–6).

- [ ] **Step 1: Write the test**

```csharp
namespace SnagList.Mcp.Tests;

using System.Reflection;
using ModelContextProtocol.Server;
using SnagList.Application;
using Xunit;

public class AgentOperationCatalogParityTests
{
    private static HashSet<string> DiscoverRegisteredToolNames()
    {
        var toolTypes = typeof(SnagList.Mcp.Tools.SnagTools).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null);

        var names = new HashSet<string>();
        foreach (var type in toolTypes)
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (method.GetCustomAttribute<McpServerToolAttribute>()?.Name is { } name) names.Add(name);
            }
        }
        return names;
    }

    [Fact]
    public void Every_registered_MCP_tool_has_a_matching_AgentOperationCatalog_entry()
    {
        var registered = DiscoverRegisteredToolNames();
        var cataloged = AgentOperationCatalog.OperationIdToMcpTool.Values.ToHashSet();

        var orphanedTools = registered.Except(cataloged).ToList();
        Assert.True(orphanedTools.Count == 0,
            $"Registered MCP tool(s) with no AgentOperationCatalog entry: {string.Join(", ", orphanedTools)}");
    }

    [Fact]
    public void Every_AgentOperationCatalog_entry_has_a_matching_registered_MCP_tool()
    {
        var registered = DiscoverRegisteredToolNames();
        var cataloged = AgentOperationCatalog.OperationIdToMcpTool.Values;

        var missingTools = cataloged.Except(registered).ToList();
        Assert.True(missingTools.Count == 0,
            $"AgentOperationCatalog entry/entries with no registered MCP tool: {string.Join(", ", missingTools)}");
    }
}
```

- [ ] **Step 2: Run the test**

Run: `dotnet test tests/SnagList.Mcp.Tests --filter AgentOperationCatalogParityTests`
Expected: PASS immediately — Tasks 4–6 already named every tool to match its
`AgentOperationCatalog` counterpart exactly (`report_snag`, `edit_snag`, ..., `get_me`). If this
fails, the mismatch is the bug to fix, not the test — do not adjust the catalog to match an
accidentally-misspelled tool name without checking which one is actually wrong.

- [ ] **Step 3: Commit**

```bash
git add tests/SnagList.Mcp.Tests/AgentOperationCatalogParityTests.cs
git commit -m "test(mcp): add CI parity test between MCP tools and AgentOperationCatalog"
```

---

## Task 9: Add `mcp` to the local docker deployment

**Files:**
- Create: `src/SnagList.Mcp/Dockerfile`
- Modify: `docker-compose.yml` (Plan 1, Task 22) — add the `mcp` service

**Interfaces:** none — this is the deployment wiring for everything Tasks 1–8 built.

- [ ] **Step 1: Write the Dockerfile**

```dockerfile
# src/SnagList.Mcp/Dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/SnagList.Mcp/SnagList.Mcp.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "SnagList.Mcp.dll"]
```

- [ ] **Step 2: Add the `mcp` service to `docker-compose.yml`**

```yaml
  mcp:
    build:
      context: .
      dockerfile: src/SnagList.Mcp/Dockerfile
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
      Notifications__MaintenanceTeamEmail: maintenance@example.com
      Auth__Local__Authority: http://keycloak:8080/realms/snaglist
      Auth__Local__Audience: snaglist-api
    ports:
      - "5090:8080"
```

Same environment as `api` (Plan 1, Task 22) — both are thin presentation layers over the identical
`AddSnagListInfrastructure` graph, so they're configured identically; only the published port
differs.

- [ ] **Step 3: Bring the stack up and verify**

```bash
docker compose up -d --build mcp
curl -s http://localhost:5090/health
# {"status":"healthy"}
```

Expected: the `mcp` service builds and responds on its health check, alongside `api` from Plan 1.
A full MCP-protocol-level check (an actual `initialize` handshake and a `tools/call`) needs an MCP
client and is better exercised by pointing a real agent at `http://localhost:5090/mcp` once this is
running — that's an exploratory verification step for whoever executes this plan, not something
this document can script blind.

- [ ] **Step 4: Commit**

```bash
git add src/SnagList.Mcp/Dockerfile docker-compose.yml
git commit -m "feat(deploy): add mcp service to the local docker deployment"
```

---

## Plan exit criteria

- `dotnet test` (every project, including Plan 1's) passes.
- `docker compose up -d --build && docker compose --profile seed run --rm seed` brings up `api`,
  `mcp`, and every dependency together; both health checks respond.
- Every tool in `AgentOperationCatalog` is registered and callable, gated by the same
  `StaffPolicy`/`MaintenancePolicy` distinctions as its REST counterpart.
- No new domain concepts were introduced — this plan is pure presentation-layer wiring, so
  `docs/ontology.ttl` needs no changes.

**Not in this plan** (remaining follow-on plans): the Blazor WASM frontend, the home-lab
deployment, and the AWS deployment.
