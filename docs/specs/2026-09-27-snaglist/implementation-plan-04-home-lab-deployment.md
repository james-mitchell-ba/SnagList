# SnagList Home-Lab Deployment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deploy `api`/`mcp`/`web` to the home-lab's shared infrastructure — its existing,
persistent Keycloak instance and its own dedicated MinIO — pulling prebuilt images from GHCR rather
than building on the host, per 06-deployment-home-lab.md.

**Architecture:** Structurally the same three app services as Plan 1's local docker-compose, minus
the throwaway Keycloak container (there's a real, persistent one already running on the home-lab,
owned outside this repo) and minus host-building (images are pulled by tag). The one genuinely new
piece of logic this plan adds: local docker's Keycloak imports its realm at container startup via
`--import-realm` (Plan 1, Task 22) — that mechanism doesn't exist for an instance this repo doesn't
own and can't restart. This plan builds the equivalent as an idempotent reconciliation against
Keycloak's Admin REST API, run from `SnagList.SeedData`.

**Tech Stack:** Keycloak's Admin REST API (bearer-token admin-cli password grant, `GET`/`DELETE`/
`POST /admin/realms/{realm}`) via plain `HttpClient`; `Testcontainers.Keycloak` for testing it
against a real instance; GitHub Actions + GHCR for image publishing.

**Spec:** [docs/specs/2026-09-27-snaglist/](README.md), primarily 06-deployment-home-lab.md. Builds
on [implementation-plan-01-core-domain-api.md](implementation-plan-01-core-domain-api.md) (`api`,
`SnagList.SeedData`, the local realm-export this plan adapts) and
[implementation-plan-03-blazor-web.md](implementation-plan-03-blazor-web.md) (`web`). Plan 2's
`mcp` service is included in the compose file but needs no home-lab-specific changes of its own.

## Global Constraints

- Nothing here builds container images on the host — every app service pulls a tag from GHCR
  (06-deployment-home-lab.md: "prebuilt images... rather than built on the host").
- No hardcoded credentials anywhere in `deploy/home-lab/` — every secret is a required environment
  variable (`${VAR:?}`, no default), unlike local docker's throwaway convenience passwords (Plan 1,
  Task 22).
- The realm reconciliation only ever touches the `snaglist` realm on the shared Keycloak instance —
  it must never delete, list, or otherwise touch any other realm that instance hosts.
- `db` and `minio` join only the internal `snaglist-private` network — never the shared ingress
  network, never a host-published port. `mailpit` is the one deliberate exception (matching
  JointBooking's own "expose Mailpit publicly for home-lab demo visibility" precedent): it joins
  both networks so its web UI can be reached, but only because the shared ingress's own routing
  (outside this repo) chooses to proxy its UI port and not its raw SMTP port — this compose file
  doesn't and can't restrict that per-port itself.
- No placeholder code, no `TODO`s left in committed code — every task ships working, tested code.

---

## Task 1: `KeycloakRealmConverger` — idempotent realm reconciliation

**Files:**
- Create: `src/SnagList.SeedData/Keycloak/KeycloakAdminOptions.cs`
- Create: `src/SnagList.SeedData/Keycloak/KeycloakRealmConverger.cs`
- Test: `tests/SnagList.SeedData.Tests/Keycloak/KeycloakFixture.cs`
- Test: `tests/SnagList.SeedData.Tests/Keycloak/KeycloakRealmConvergerTests.cs`

**Interfaces:**
- Produces: `KeycloakRealmConverger.ConvergeAsync(realmExportJson, reseed, ct)` — consumed by
  `SnagList.SeedData/Program.cs` in Task 2.

- [ ] **Step 1: Add the packages**

```bash
dotnet add src/SnagList.SeedData package Microsoft.Extensions.Http
dotnet add tests/SnagList.SeedData.Tests package Testcontainers.Keycloak
```

- [ ] **Step 2: Write the Testcontainers fixture**

```csharp
namespace SnagList.SeedData.Tests.Keycloak;

using Testcontainers.Keycloak;
using Xunit;

public sealed class KeycloakFixture : IAsyncLifetime
{
    // Set explicitly via WithEnvironment (a base Testcontainers builder method every module
    // supports) rather than relying on this module's own default-credential accessors, whose
    // exact names aren't certain enough to depend on here.
    public const string AdminUsername = "admin";
    public const string AdminPassword = "admin";

    private readonly KeycloakContainer _container = new KeycloakBuilder()
        .WithEnvironment("KEYCLOAK_ADMIN", AdminUsername)
        .WithEnvironment("KEYCLOAK_ADMIN_PASSWORD", AdminPassword)
        .Build();

    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public string BaseAddress => _container.GetBaseAddress();
}

[CollectionDefinition("Keycloak")]
public sealed class KeycloakCollection : ICollectionFixture<KeycloakFixture>;
```

- [ ] **Step 3: Write the failing tests**

```csharp
namespace SnagList.SeedData.Tests.Keycloak;

using System.Net;
using System.Net.Http.Json;
using SnagList.SeedData.Keycloak;
using Xunit;

[Collection("Keycloak")]
public class KeycloakRealmConvergerTests(KeycloakFixture fixture)
{
    private const string TestRealmJson = """
    { "realm": "snaglist-test", "enabled": true, "roles": { "realm": [{ "name": "Staff" }] } }
    """;

    private KeycloakRealmConverger BuildConverger() => new(new HttpClient(), new KeycloakAdminOptions
    {
        AdminUrl = fixture.BaseAddress,
        AdminUsername = KeycloakFixture.AdminUsername,
        AdminPassword = KeycloakFixture.AdminPassword,
    });

    [Fact]
    public async Task ConvergeAsync_creates_the_realm_when_it_does_not_exist()
    {
        var converger = BuildConverger();

        await converger.ConvergeAsync(TestRealmJson, reseed: false, default);

        using var client = new HttpClient { BaseAddress = new Uri(fixture.BaseAddress) };
        var token = await GetAdminTokenAsync(client);
        var response = await client.SendAsync(AuthedGet($"{fixture.BaseAddress}/admin/realms/snaglist-test", token));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ConvergeAsync_without_reseed_leaves_an_existing_realm_untouched()
    {
        var converger = BuildConverger();
        await converger.ConvergeAsync(TestRealmJson, reseed: false, default);

        // A second convergence without --reseed must not error even though the realm already exists.
        await converger.ConvergeAsync(TestRealmJson, reseed: false, default);
    }

    [Fact]
    public async Task ConvergeAsync_with_reseed_deletes_and_recreates_the_realm()
    {
        var converger = BuildConverger();
        await converger.ConvergeAsync(TestRealmJson, reseed: false, default);

        await converger.ConvergeAsync(TestRealmJson, reseed: true, default);

        using var client = new HttpClient { BaseAddress = new Uri(fixture.BaseAddress) };
        var token = await GetAdminTokenAsync(client);
        var response = await client.SendAsync(AuthedGet($"{fixture.BaseAddress}/admin/realms/snaglist-test", token));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static HttpRequestMessage AuthedGet(string url, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new("Bearer", token);
        return request;
    }

    private static async Task<string> GetAdminTokenAsync(HttpClient client)
    {
        var response = await client.PostAsync($"{client.BaseAddress}realms/master/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "admin-cli",
                ["username"] = KeycloakFixture.AdminUsername,
                ["password"] = KeycloakFixture.AdminPassword,
            }));
        var body = await response.Content.ReadFromJsonAsync<TokenResponse>();
        return body!.AccessToken;
    }

    private sealed record TokenResponse(string AccessToken);
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test tests/SnagList.SeedData.Tests --filter KeycloakRealmConvergerTests`
Expected: FAIL — `KeycloakRealmConverger`/`KeycloakAdminOptions` don't exist. (Requires Docker
running locally — Testcontainers pulls and starts a real Keycloak image.)

- [ ] **Step 5: Implement**

```csharp
namespace SnagList.SeedData.Keycloak;

public sealed class KeycloakAdminOptions
{
    public string AdminUrl { get; set; } = "";
    public string AdminUsername { get; set; } = "";
    public string AdminPassword { get; set; } = "";
}
```

```csharp
namespace SnagList.SeedData.Keycloak;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

public sealed class KeycloakRealmConverger(HttpClient httpClient, KeycloakAdminOptions options)
{
    private sealed record TokenResponse(string AccessToken);

    public async Task ConvergeAsync(string realmExportJson, bool reseed, CancellationToken ct)
    {
        var realmDocument = JsonDocument.Parse(realmExportJson);
        var realmName = realmDocument.RootElement.GetProperty("realm").GetString()
            ?? throw new InvalidOperationException("Realm export JSON has no 'realm' property.");

        var token = await GetAdminTokenAsync(ct);
        var exists = await RealmExistsAsync(realmName, token, ct);

        if (exists && !reseed)
        {
            return;
        }
        if (exists)
        {
            await DeleteRealmAsync(realmName, token, ct);
        }
        await CreateRealmAsync(realmExportJson, token, ct);
    }

    private async Task<string> GetAdminTokenAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"{options.AdminUrl}/realms/master/protocol/openid-connect/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "admin-cli",
                ["username"] = options.AdminUsername,
                ["password"] = options.AdminPassword,
            }),
        };
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct);
        return body!.AccessToken;
    }

    private async Task<bool> RealmExistsAsync(string realmName, string token, CancellationToken ct)
    {
        using var request = Authed(HttpMethod.Get, $"{options.AdminUrl}/admin/realms/{realmName}", token);
        var response = await httpClient.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return false;
        response.EnsureSuccessStatusCode();
        return true;
    }

    private async Task DeleteRealmAsync(string realmName, string token, CancellationToken ct)
    {
        using var request = Authed(HttpMethod.Delete, $"{options.AdminUrl}/admin/realms/{realmName}", token);
        (await httpClient.SendAsync(request, ct)).EnsureSuccessStatusCode();
    }

    private async Task CreateRealmAsync(string realmExportJson, string token, CancellationToken ct)
    {
        using var request = Authed(HttpMethod.Post, $"{options.AdminUrl}/admin/realms", token);
        request.Content = new StringContent(realmExportJson, System.Text.Encoding.UTF8, "application/json");
        (await httpClient.SendAsync(request, ct)).EnsureSuccessStatusCode();
    }

    private static HttpRequestMessage Authed(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new("Bearer", token);
        return request;
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/SnagList.SeedData.Tests --filter KeycloakRealmConvergerTests`
Expected: PASS (3 tests).

- [ ] **Step 7: Commit**

```bash
git add src/SnagList.SeedData/Keycloak tests/SnagList.SeedData.Tests/Keycloak
git commit -m "feat(seed-data): add KeycloakRealmConverger for idempotent realm reconciliation"
```

---

## Task 2: Wire `--reseed` and realm convergence into `SnagList.SeedData`

Extends `SeedRunner` (Plan 1, Task 21) with the wipe-and-reseed behavior 06-deployment-home-lab.md
describes — Plan 1 never needed it, since local docker's disposable Keycloak container handles its
own reset by just restarting. Home-lab's persistent database and persistent Keycloak both need an
explicit, idempotent "start over" path instead.

**Files:**
- Modify: `src/SnagList.SeedData/SeedRunner.cs` — add a `reseed` parameter
- Modify: `tests/SnagList.SeedData.Tests/SeedRunnerTests.cs` — add a reseed test case
- Modify: `src/SnagList.SeedData/Program.cs` — parse `--reseed`, run realm convergence when
  `Keycloak__ManageRealm=true`

**Interfaces:**
- Consumes: `KeycloakRealmConverger` (Task 1).
- Produces: `SeedRunner.RunAsync(dbContext, output, reseed: bool)` — the shape Task 5's
  `docker-compose.yml` `seed` service entrypoint calls (via `--reseed` on the command line).

- [ ] **Step 1: Write the failing reseed test**

```csharp
// Append to SeedRunnerTests (tests/SnagList.SeedData.Tests/SeedRunnerTests.cs)

[Fact]
public async Task RunAsync_with_reseed_wipes_existing_data_before_reseeding()
{
    await using (var firstRun = fixture.CreateContext()) await SeedRunner.RunAsync(firstRun, TextWriter.Null);
    await using var firstRunReadContext = fixture.CreateContext();
    var firstLocationId = (await firstRunReadContext.Locations.FirstAsync()).Id;

    await using var reseedContext = fixture.CreateContext();
    await SeedRunner.RunAsync(reseedContext, TextWriter.Null, reseed: true);

    await using var afterReseed = fixture.CreateContext();
    Assert.Equal(3, await afterReseed.Locations.CountAsync());
    // A genuinely fresh set of rows, not the same ones re-detected as "already present."
    Assert.DoesNotContain(await afterReseed.Locations.Select(l => l.Id).ToListAsync(), id => id == firstLocationId);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/SnagList.SeedData.Tests --filter RunAsync_with_reseed_wipes_existing_data_before_reseeding`
Expected: FAIL — `RunAsync` has no `reseed` parameter yet.

- [ ] **Step 3: Add the `reseed` parameter to `SeedRunner`**

Replace `SeedRunner.RunAsync`'s signature and body (Plan 1, Task 21) with:

```csharp
public static async Task RunAsync(SnagListDbContext dbContext, TextWriter output, bool reseed = false)
{
    await dbContext.Database.MigrateAsync();

    if (reseed)
    {
        output.WriteLine("Reseeding: wiping existing data...");
        await dbContext.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE snag_comments, snag_photos, snags, locations, audit_log_entries RESTART IDENTITY CASCADE");
    }
    else if (await dbContext.Locations.AnyAsync())
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
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/SnagList.SeedData.Tests`
Expected: PASS — both the original idempotency test and the new reseed test.

- [ ] **Step 5: Rewrite `Program.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using SnagList.Infrastructure.Persistence;
using SnagList.SeedData;
using SnagList.SeedData.Keycloak;

var reseed = args.Contains("--reseed");

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__SnagList")
    ?? throw new InvalidOperationException("ConnectionStrings__SnagList is required.");
var dbOptions = new DbContextOptionsBuilder<SnagListDbContext>().UseNpgsql(connectionString).Options;
await using var dbContext = new SnagListDbContext(dbOptions);

await SeedRunner.RunAsync(dbContext, Console.Out, reseed);

if (Environment.GetEnvironmentVariable("Keycloak__ManageRealm") == "true")
{
    var adminOptions = new KeycloakAdminOptions
    {
        AdminUrl = Environment.GetEnvironmentVariable("Keycloak__AdminUrl")
            ?? throw new InvalidOperationException("Keycloak__AdminUrl is required."),
        AdminUsername = Environment.GetEnvironmentVariable("Keycloak__AdminUsername")
            ?? throw new InvalidOperationException("Keycloak__AdminUsername is required."),
        AdminPassword = Environment.GetEnvironmentVariable("Keycloak__AdminPassword")
            ?? throw new InvalidOperationException("Keycloak__AdminPassword is required."),
    };
    var realmExportPath = Environment.GetEnvironmentVariable("Keycloak__RealmExportPath")
        ?? throw new InvalidOperationException("Keycloak__RealmExportPath is required.");
    var realmExportJson = await File.ReadAllTextAsync(realmExportPath);

    Console.WriteLine(reseed ? "Reseeding the Keycloak realm..." : "Converging the Keycloak realm...");
    await new KeycloakRealmConverger(new HttpClient(), adminOptions).ConvergeAsync(realmExportJson, reseed, default);
    Console.WriteLine("Keycloak realm convergence complete.");
}
```

`Keycloak__ManageRealm` defaults to unset/false, so local docker's `seed` service (Plan 1, Task 22,
whose compose file never sets this variable) is completely unaffected — this task only adds new,
opt-in behavior.

- [ ] **Step 6: Run the full solution test suite to confirm no regressions**

Run: `dotnet test`
Expected: PASS — every project, Plans 1–3 and this task's additions.

- [ ] **Step 7: Commit**

```bash
git add src/SnagList.SeedData tests/SnagList.SeedData.Tests
git commit -m "feat(seed-data): wire --reseed and opt-in Keycloak realm convergence"
```

---

## Task 3: Home-lab Keycloak realm export

Combines both clients from Plan 1 (`snaglist-api`) and Plan 3 (`snaglist-web` + its audience
scope) into one file, with real home-lab hostnames instead of `localhost`. The placeholder hostname
`https://snaglist.home.example` **must be replaced with the home-lab's actual public hostname**
before this realm is ever converged against the real shared instance — it is not usable as-is,
unlike local docker's realm export which really does work unmodified.

**Files:**
- Create: `deploy/home-lab/realm-export.json`

**Interfaces:** none — a data file `docker-compose.yml` (Task 5) mounts and
`KeycloakRealmConverger` (Task 1) reads.

- [ ] **Step 1: Write the file**

```json
{
  "realm": "snaglist",
  "enabled": true,
  "sslRequired": "external",
  "roles": {
    "realm": [
      { "name": "Staff", "description": "Can report and view Snags" },
      { "name": "Maintenance", "description": "Can triage, resolve, and manage Locations" }
    ]
  },
  "clientScopes": [
    {
      "name": "snaglist-api-audience",
      "protocol": "openid-connect",
      "protocolMappers": [
        {
          "name": "snaglist-api-audience-mapper",
          "protocol": "openid-connect",
          "protocolMapper": "oidc-audience-mapper",
          "config": {
            "included.client.audience": "snaglist-api",
            "id.token.claim": "false",
            "access.token.claim": "true"
          }
        }
      ]
    }
  ],
  "clients": [
    {
      "clientId": "snaglist-api",
      "enabled": true,
      "publicClient": false,
      "secret": "${SNAGLIST_KEYCLOAK_CLIENT_SECRET}",
      "protocol": "openid-connect",
      "standardFlowEnabled": true,
      "directAccessGrantsEnabled": true,
      "redirectUris": ["https://snaglist.home.example/*"],
      "webOrigins": ["https://snaglist.home.example"],
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
    },
    {
      "clientId": "snaglist-web",
      "enabled": true,
      "publicClient": true,
      "protocol": "openid-connect",
      "standardFlowEnabled": true,
      "directAccessGrantsEnabled": false,
      "redirectUris": ["https://snaglist.home.example/authentication/login-callback", "https://snaglist.home.example/"],
      "webOrigins": ["https://snaglist.home.example"],
      "attributes": { "pkce.code.challenge.method": "S256" },
      "defaultClientScopes": ["snaglist-api-audience", "profile", "email", "roles"]
    }
  ]
}
```

`"secret": "${SNAGLIST_KEYCLOAK_CLIENT_SECRET}"` is a literal placeholder string, not shell/env
interpolation — Keycloak's realm import doesn't expand it. Task 5 documents the actual mechanism:
the real secret is generated once and pasted into this file locally before it's used, and the file
is never committed with a real secret in it (see Task 5's `.gitignore` note).

Note there is deliberately no `users` array here, unlike Plan 1's local realm export — home-lab is
a real, persistent instance, so real staff accounts are provisioned by the home-lab operator through
Keycloak's own admin console, not seeded as demo credentials in a checked-in file.

- [ ] **Step 2: Commit**

```bash
git add deploy/home-lab/realm-export.json
git commit -m "feat(deploy): add the home-lab Keycloak realm export"
```

---

## Task 4: Publish container images to GHCR

**Files:**
- Create: `.github/workflows/publish-images.yml`

**Interfaces:** none — produces the images `docker-compose.yml` (Task 5) references by tag.

- [ ] **Step 1: Write the workflow**

```yaml
name: Publish container images

on:
  push:
    branches: [main]
    paths-ignore:
      - 'docs/**'
      - '**.md'

permissions:
  contents: read
  packages: write

jobs:
  publish:
    runs-on: ubuntu-latest
    strategy:
      matrix:
        include:
          - service: api
            dockerfile: src/SnagList.Api/Dockerfile
          - service: mcp
            dockerfile: src/SnagList.Mcp/Dockerfile
          - service: web
            dockerfile: src/SnagList.Web/Dockerfile
          - service: seed
            dockerfile: src/SnagList.SeedData/Dockerfile
    steps:
      - uses: actions/checkout@v4

      - uses: docker/login-action@v3
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}

      - uses: docker/build-push-action@v6
        with:
          context: .
          file: ${{ matrix.dockerfile }}
          push: true
          tags: |
            ghcr.io/${{ github.repository_owner }}/snaglist-${{ matrix.service }}:${{ github.sha }}
            ghcr.io/${{ github.repository_owner }}/snaglist-${{ matrix.service }}:latest
```

Four images (`api`, `mcp`, `web`, `seed`) — the same four deployables Plan 1's local docker
builds from source; home-lab (Task 5) and the AWS plan both pull these instead of building.

- [ ] **Step 2: Commit**

```bash
git add .github/workflows/publish-images.yml
git commit -m "feat(ci): publish api/mcp/web/seed images to GHCR on push to main"
```

---

## Task 5: `deploy/home-lab/docker-compose.yml`

Every secret here is a required environment variable (`${VAR:?}`) — there is no home-lab
equivalent of local docker's hardcoded `minioadmin`/`admin` convenience passwords, because this
isn't a disposable environment.

**Files:**
- Create: `deploy/home-lab/docker-compose.yml`
- Create: `deploy/home-lab/.env.example`
- Modify: `.gitignore` — ensure `deploy/home-lab/.env` is ignored (the real secrets file, never
  committed)

**Interfaces:** none — this is the deployment target itself; nothing in the repo consumes it.

- [ ] **Step 1: Write `.env.example`**

```bash
# deploy/home-lab/.env.example — copy to deploy/home-lab/.env and fill in real values.
# .env is gitignored; never commit real secrets.

SNAGLIST_GHCR_OWNER=james-mitchell-ba
SNAGLIST_IMAGE_TAG=latest

SNAGLIST_DB_PASSWORD=
SNAGLIST_MINIO_ROOT_USER=
SNAGLIST_MINIO_ROOT_PASSWORD=
SNAGLIST_MAINTENANCE_EMAIL=maintenance@snaglist.home.example

SNAGLIST_KEYCLOAK_AUTHORITY=https://keycloak.home.example/realms/snaglist
SNAGLIST_KEYCLOAK_ADMIN_URL=https://keycloak.home.example
SNAGLIST_KEYCLOAK_ADMIN_USERNAME=
SNAGLIST_KEYCLOAK_ADMIN_PASSWORD=
SNAGLIST_KEYCLOAK_CLIENT_SECRET=

# The docker network the home-lab's own ingress (reverse proxy) already publishes — created
# outside this compose file, by the home-lab's own infrastructure.
SNAGLIST_INGRESS_NETWORK=homelab-ingress
```

- [ ] **Step 2: Write `docker-compose.yml`**

```yaml
services:
  db:
    image: postgres:16-alpine
    environment:
      POSTGRES_DB: snaglist
      POSTGRES_USER: snaglist
      POSTGRES_PASSWORD: ${SNAGLIST_DB_PASSWORD:?}
    volumes:
      - snaglist-db-data:/var/lib/postgresql/data
    networks: [snaglist-private]

  minio:
    image: minio/minio:latest
    command: server /data --console-address ":9001"
    environment:
      MINIO_ROOT_USER: ${SNAGLIST_MINIO_ROOT_USER:?}
      MINIO_ROOT_PASSWORD: ${SNAGLIST_MINIO_ROOT_PASSWORD:?}
    volumes:
      - snaglist-minio-data:/data
    networks: [snaglist-private]

  minio-bootstrap:
    image: minio/mc:latest
    depends_on: [minio]
    networks: [snaglist-private]
    entrypoint: >
      /bin/sh -c "
      mc alias set local http://minio:9000 ${SNAGLIST_MINIO_ROOT_USER:?} ${SNAGLIST_MINIO_ROOT_PASSWORD:?} &&
      mc mb --ignore-existing local/snaglist-photos
      "

  mailpit:
    image: axllent/mailpit:latest
    networks: [snaglist-private, snaglist-public]

  api:
    image: ghcr.io/${SNAGLIST_GHCR_OWNER:?}/snaglist-api:${SNAGLIST_IMAGE_TAG:?}
    depends_on: [db, minio]
    networks: [snaglist-private, snaglist-public]
    environment:
      ConnectionStrings__SnagList: "Host=db;Database=snaglist;Username=snaglist;Password=${SNAGLIST_DB_PASSWORD:?}"
      Storage__BucketName: snaglist-photos
      Storage__AccessKey: ${SNAGLIST_MINIO_ROOT_USER:?}
      Storage__SecretKey: ${SNAGLIST_MINIO_ROOT_PASSWORD:?}
      Storage__ServiceUrl: http://minio:9000
      Email__Host: mailpit
      Email__Port: "1025"
      Email__FromAddress: snaglist@home.example
      Email__UseTls: "false"
      Notifications__MaintenanceTeamEmail: ${SNAGLIST_MAINTENANCE_EMAIL:?}
      Auth__Local__Authority: ${SNAGLIST_KEYCLOAK_AUTHORITY:?}
      Auth__Local__Audience: snaglist-api

  mcp:
    image: ghcr.io/${SNAGLIST_GHCR_OWNER:?}/snaglist-mcp:${SNAGLIST_IMAGE_TAG:?}
    depends_on: [db, minio]
    networks: [snaglist-private, snaglist-public]
    environment:
      ConnectionStrings__SnagList: "Host=db;Database=snaglist;Username=snaglist;Password=${SNAGLIST_DB_PASSWORD:?}"
      Storage__BucketName: snaglist-photos
      Storage__AccessKey: ${SNAGLIST_MINIO_ROOT_USER:?}
      Storage__SecretKey: ${SNAGLIST_MINIO_ROOT_PASSWORD:?}
      Storage__ServiceUrl: http://minio:9000
      Email__Host: mailpit
      Email__Port: "1025"
      Email__FromAddress: snaglist@home.example
      Email__UseTls: "false"
      Notifications__MaintenanceTeamEmail: ${SNAGLIST_MAINTENANCE_EMAIL:?}
      Auth__Local__Authority: ${SNAGLIST_KEYCLOAK_AUTHORITY:?}
      Auth__Local__Audience: snaglist-api

  web:
    image: ghcr.io/${SNAGLIST_GHCR_OWNER:?}/snaglist-web:${SNAGLIST_IMAGE_TAG:?}
    networks: [snaglist-public]

  seed:
    image: ghcr.io/${SNAGLIST_GHCR_OWNER:?}/snaglist-seed:${SNAGLIST_IMAGE_TAG:?}
    profiles: ["seed", "reseed"]
    depends_on: [db]
    networks: [snaglist-private]
    volumes:
      - ./realm-export.json:/app/realm-export.json:ro
    environment:
      ConnectionStrings__SnagList: "Host=db;Database=snaglist;Username=snaglist;Password=${SNAGLIST_DB_PASSWORD:?}"
      Keycloak__ManageRealm: "true"
      Keycloak__AdminUrl: ${SNAGLIST_KEYCLOAK_ADMIN_URL:?}
      Keycloak__AdminUsername: ${SNAGLIST_KEYCLOAK_ADMIN_USERNAME:?}
      Keycloak__AdminPassword: ${SNAGLIST_KEYCLOAK_ADMIN_PASSWORD:?}
      Keycloak__RealmExportPath: /app/realm-export.json

networks:
  snaglist-private:
    internal: true
  snaglist-public:
    external: true
    name: ${SNAGLIST_INGRESS_NETWORK:?}

volumes:
  snaglist-db-data:
  snaglist-minio-data:
```

`seed` runs under either the `seed` or `reseed` compose profile; which behavior it takes
(idempotent-skip vs. wipe-and-recreate) is controlled by the `--reseed` command-line argument
(Task 2), passed at `docker compose run` time (Step 4), not by which profile selected it — the two
profiles exist only so an operator can `docker compose --profile seed up` without accidentally also
matching a `--profile reseed` filter elsewhere, not because the profiles themselves branch behavior.

- [ ] **Step 3: Ensure `.env` is gitignored**

Add to `.gitignore` (create it if it doesn't already exist at the repo root):

```
deploy/home-lab/.env
```

- [ ] **Step 4: Commit**

```bash
git add deploy/home-lab/docker-compose.yml deploy/home-lab/.env.example .gitignore
git commit -m "feat(deploy): add the home-lab docker-compose stack"
```

---

## Task 6: Manual verification against a stand-in shared Keycloak

There's no real home-lab available to test against from here, and Task 1's Testcontainers tests
already prove `KeycloakRealmConverger`'s logic in isolation — what's still unverified is the *whole
stack* wired together the way Task 5's compose file actually assembles it. This task stands up a
throwaway Keycloak to play the role of "the home-lab's existing instance," strictly for this
verification — it is never part of the real deployment.

**Files:**
- Create: `deploy/home-lab/dev-verification/docker-compose.stand-in-keycloak.yml`

**Interfaces:** none — a disposable local stand-in, deleted in spirit (kept in the repo only as a
reusable verification harness, clearly out-of-band from the real deployment) after this task.

- [ ] **Step 1: Write the stand-in**

```yaml
# FOR VERIFYING THIS PLAN ONLY. Simulates "the home-lab's existing shared Keycloak" so the
# reconciliation logic and the full stack can be exercised locally without a real home-lab.
# The real home-lab deployment (deploy/home-lab/docker-compose.yml) never uses this file — its
# Keycloak already exists, persistently, owned outside this repo.
services:
  stand-in-keycloak:
    image: quay.io/keycloak/keycloak:26.0
    command: start-dev
    environment:
      KEYCLOAK_ADMIN: admin
      KEYCLOAK_ADMIN_PASSWORD: admin
    ports:
      - "8180:8080"
    networks: [homelab-ingress-stand-in]

networks:
  homelab-ingress-stand-in:
    name: homelab-ingress-stand-in
```

- [ ] **Step 2: Bring up the stand-in, then the real home-lab compose pointed at it**

```bash
docker compose -f deploy/home-lab/dev-verification/docker-compose.stand-in-keycloak.yml up -d

cp deploy/home-lab/.env.example deploy/home-lab/.env
# Edit deploy/home-lab/.env:
#   SNAGLIST_KEYCLOAK_AUTHORITY=http://stand-in-keycloak:8080/realms/snaglist
#   SNAGLIST_KEYCLOAK_ADMIN_URL=http://stand-in-keycloak:8080
#   SNAGLIST_KEYCLOAK_ADMIN_USERNAME=admin
#   SNAGLIST_KEYCLOAK_ADMIN_PASSWORD=admin
#   SNAGLIST_INGRESS_NETWORK=homelab-ingress-stand-in
#   (fill in SNAGLIST_DB_PASSWORD / SNAGLIST_MINIO_ROOT_USER / SNAGLIST_MINIO_ROOT_PASSWORD / SNAGLIST_MAINTENANCE_EMAIL with any values)
# Also replace https://snaglist.home.example with http://localhost:8180-fronted-equivalent in
# deploy/home-lab/realm-export.json for this local run only — do not commit that edit.

docker compose --env-file deploy/home-lab/.env -f deploy/home-lab/docker-compose.yml \
  --profile seed run --rm seed
```

Expected: logs show "Seeding demo data..." / "Seed complete." and "Converging the Keycloak
realm..." / "Keycloak realm convergence complete."

- [ ] **Step 3: Verify the realm was actually created**

```bash
curl -s -X POST http://localhost:8180/realms/master/protocol/openid-connect/token \
  -d grant_type=password -d client_id=admin-cli -d username=admin -d password=admin \
  | jq -r .access_token > /tmp/admin-token

curl -s http://localhost:8180/admin/realms/snaglist \
  -H "Authorization: Bearer $(cat /tmp/admin-token)" | jq '.realm, .clients[].clientId'
```

Expected: `"snaglist"`, and both `"snaglist-api"` and `"snaglist-web"` listed.

- [ ] **Step 4: Verify `--reseed` deletes and recreates rather than erroring on an existing realm**

```bash
docker compose --env-file deploy/home-lab/.env -f deploy/home-lab/docker-compose.yml \
  --profile reseed run --rm seed dotnet SnagList.SeedData.dll --reseed
```

Expected: succeeds; re-running Step 3's `curl` still finds the realm afterward.

- [ ] **Step 5: Bring up the rest of the stack and verify `api`'s health check**

```bash
docker compose --env-file deploy/home-lab/.env -f deploy/home-lab/docker-compose.yml up -d
curl -s http://localhost:5080/health 2>/dev/null || echo "api has no host-published port here — check via 'docker compose exec api curl localhost:8080/health' instead, since Task 5's compose deliberately doesn't publish api/mcp to the host the way local docker does (Plan 1) - only the shared ingress network reaches them."
docker compose --env-file deploy/home-lab/.env -f deploy/home-lab/docker-compose.yml exec api curl -s http://localhost:8080/health
```

Expected: `{"status":"healthy"}` from the `exec`-based check.

- [ ] **Step 6: Tear down the verification environment**

```bash
docker compose --env-file deploy/home-lab/.env -f deploy/home-lab/docker-compose.yml down -v
docker compose -f deploy/home-lab/dev-verification/docker-compose.stand-in-keycloak.yml down -v
rm deploy/home-lab/.env
```

- [ ] **Step 7: Commit**

```bash
git add deploy/home-lab/dev-verification
git commit -m "test(deploy): add a stand-in Keycloak harness for verifying the home-lab stack"
```

---

## Plan exit criteria

- `dotnet test` (every project, including Plans 1–3's) passes.
- Task 6's manual verification succeeds: the realm converges against a Keycloak this repo doesn't
  own, `--reseed` deletes and recreates it without touching anything else on that instance, and
  `api` comes up healthy talking to it.
- No hardcoded secrets exist anywhere under `deploy/home-lab/` — grep for `minioadmin`/`admin`/
  `password` as a final check before merging.
- No new domain concepts were introduced — this plan is pure deployment/infrastructure wiring, so
  `docs/ontology.ttl` needs no changes.

**Not in this plan** (remaining follow-on plan): the AWS deployment.
