# SnagList Blazor Web Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A standalone Blazor WASM app (`SnagList.Web`) that staff and maintenance actually use day
to day — report a `Snag`, browse and triage the list, manage `Location`s — authenticated against
Keycloak, talking to the REST API from Plan 1.

**Architecture:** `SnagList.Web` is a pure client: no server-side rendering, no project reference to
`SnagList.Api` (a WASM app can't reference an ASP.NET Core hosting project). It authenticates via
OIDC Authorization Code + PKCE against the same Keycloak instance `SnagList.Api.Auth.Local` already
validates tokens from, and talks to the REST API over `HttpClient`. The one architectural decision
this plan is actually built around: **every UI button that changes a `Snag`'s state renders because
its relation is present in that response's `_links`, never because of client-side role/status
branching** — the same HATEOAS payoff 03-api-design.md described, now visible in a browser instead
of asserted in a REST test.

**Tech Stack:** .NET 10 Blazor WebAssembly (standalone), `Microsoft.AspNetCore.Components
.WebAssembly.Authentication` (OIDC), `bunit` for component tests, xUnit for the API client.

**Spec:** [docs/specs/2026-09-27-snaglist/](README.md), primarily 03-api-design.md (the hypermedia
contract this plan consumes) and 04-security-and-authentication.md. Builds on
[implementation-plan-01-core-domain-api.md](implementation-plan-01-core-domain-api.md) (the REST
API and the local docker Keycloak realm this plan adds a client to) — nothing from Plan 2 (MCP) is
needed here.

## Global Constraints

- No client-side re-implementation of the `SnagStatus` transition graph, role checks, or any other
  business rule already enforced server-side. The UI's only job is to render what `_links` says is
  available and call it — duplicating that logic client-side is exactly the drift HATEOAS exists
  to prevent (03-api-design.md).
- `SnagList.Web` never talks to Postgres, MinIO, or anything else directly — only the REST API.
- Every response contract (`SnagDetailResponse`, `LocationResponse`, `ApiLink`, etc.) is shared with
  `SnagList.Api`, not redefined — see Task 1.
- No placeholder code, no `TODO`s left in committed code — every task ships working, tested code.

---

## Task 1: Refactor — extract `SnagList.Contracts`

`SnagList.Api`'s response/request records (Plan 1, Tasks 16–20) currently live inside
`SnagList.Api`, which `SnagList.Web` can't reference (it would pull in ASP.NET Core hosting APIs a
WASM app can't run). They move to a new dependency-free class library both projects reference —
the same "extract before a second consumer needs it" move as Plan 2's Tasks 1–2 and 7.

**Files:**
- Create: `src/SnagList.Contracts/SnagList.Contracts.csproj`
- Move: `src/SnagList.Api/Contracts/ApiLink.cs` → `src/SnagList.Contracts/ApiLink.cs`
- Move: `src/SnagList.Api/Contracts/HypermediaResource.cs` → `src/SnagList.Contracts/HypermediaResource.cs`
- Move: `src/SnagList.Api/Contracts/PagedResponse.cs` → `src/SnagList.Contracts/PagedResponse.cs`
- Move: `src/SnagList.Api/Contracts/Locations/LocationResponse.cs` →
  `src/SnagList.Contracts/Locations/LocationResponse.cs`
- Move: `src/SnagList.Api/Contracts/Locations/LocationRequests.cs` →
  `src/SnagList.Contracts/Locations/LocationRequests.cs`
- Move: `src/SnagList.Api/Contracts/Snags/SnagSummaryResponse.cs` →
  `src/SnagList.Contracts/Snags/SnagSummaryResponse.cs`
- Move: `src/SnagList.Api/Contracts/Snags/SnagDetailResponse.cs` →
  `src/SnagList.Contracts/Snags/SnagDetailResponse.cs`
- Move: `src/SnagList.Api/Contracts/Snags/SnagRequests.cs` →
  `src/SnagList.Contracts/Snags/SnagRequests.cs`
- Move: `src/SnagList.Api/Contracts/Snags/SnagLifecycleRequests.cs` →
  `src/SnagList.Contracts/Snags/SnagLifecycleRequests.cs`
- Move: `src/SnagList.Api/Endpoints/MeEndpoints.cs`'s `MeResponse` record → new file
  `src/SnagList.Contracts/MeResponse.cs`
- Modify: every file under `src/SnagList.Api/**` and `tests/SnagList.Api.Tests/**` referencing
  `SnagList.Api.Contracts*` — update the `using`
- Modify: `SnagList.sln` — add the new project

**Interfaces:**
- Produces: identical types as Plan 1 built, now under namespace `SnagList.Contracts` (and
  `SnagList.Contracts.Locations`/`.Snags`) instead of `SnagList.Api.Contracts*` — consumed by
  `SnagList.Web`'s API client (Task 3) exactly as `SnagList.Api` already consumes them.

- [ ] **Step 1: Create the project and move the files**

```bash
dotnet new classlib -o src/SnagList.Contracts -n SnagList.Contracts
rm src/SnagList.Contracts/Class1.cs

git mv src/SnagList.Api/Contracts/ApiLink.cs src/SnagList.Contracts/ApiLink.cs
git mv src/SnagList.Api/Contracts/HypermediaResource.cs src/SnagList.Contracts/HypermediaResource.cs
git mv src/SnagList.Api/Contracts/PagedResponse.cs src/SnagList.Contracts/PagedResponse.cs
mkdir -p src/SnagList.Contracts/Locations src/SnagList.Contracts/Snags
git mv src/SnagList.Api/Contracts/Locations/LocationResponse.cs src/SnagList.Contracts/Locations/LocationResponse.cs
git mv src/SnagList.Api/Contracts/Locations/LocationRequests.cs src/SnagList.Contracts/Locations/LocationRequests.cs
git mv src/SnagList.Api/Contracts/Snags/SnagSummaryResponse.cs src/SnagList.Contracts/Snags/SnagSummaryResponse.cs
git mv src/SnagList.Api/Contracts/Snags/SnagDetailResponse.cs src/SnagList.Contracts/Snags/SnagDetailResponse.cs
git mv src/SnagList.Api/Contracts/Snags/SnagRequests.cs src/SnagList.Contracts/Snags/SnagRequests.cs
git mv src/SnagList.Api/Contracts/Snags/SnagLifecycleRequests.cs src/SnagList.Contracts/Snags/SnagLifecycleRequests.cs
rmdir src/SnagList.Api/Contracts/Locations src/SnagList.Api/Contracts/Snags src/SnagList.Api/Contracts

dotnet sln add src/SnagList.Contracts
dotnet add src/SnagList.Api reference src/SnagList.Contracts
```

- [ ] **Step 2: Extract `MeResponse` out of `MeEndpoints.cs`**

`MeEndpoints.cs` (Plan 1, Task 20) declared `MeResponse` inline in the same file as the endpoint
mapping. Move just that record:

```csharp
namespace SnagList.Contracts;

using SnagList.Domain.Staff;

public sealed record MeResponse(string StaffId, string Name, string Email, IReadOnlyList<StaffRole> Roles);
```

Delete the `public sealed record MeResponse(...)` line from the bottom of
`src/SnagList.Api/Endpoints/MeEndpoints.cs`.

- [ ] **Step 3: Update namespaces and `using` directives**

In every moved file, change `namespace SnagList.Api.Contracts;` → `namespace SnagList.Contracts;`,
`namespace SnagList.Api.Contracts.Locations;` → `namespace SnagList.Contracts.Locations;`, and
`namespace SnagList.Api.Contracts.Snags;` → `namespace SnagList.Contracts.Snags;`.

Across `src/SnagList.Api/` (`Program.cs`, `Endpoints/*.cs`, `Hypermedia/*.cs`) and
`tests/SnagList.Api.Tests/` (`Endpoints/*.cs`), change every `using SnagList.Api.Contracts;` /
`using SnagList.Api.Contracts.Locations;` / `using SnagList.Api.Contracts.Snags;` to
`using SnagList.Contracts;` / `using SnagList.Contracts.Locations;` / `using SnagList.Contracts.Snags;`.

Separately: `tests/SnagList.Api.Tests/Endpoints/MeEndpointTests.cs` (Plan 1, Task 20) resolves
`MeResponse` via `using SnagList.Api.Endpoints;` — that only worked because `MeResponse` used to be
declared in that same namespace, alongside `MeEndpoints`. Now that it lives in
`SnagList.Contracts`, add `using SnagList.Contracts;` to that test file (the `SnagList.Api.Endpoints`
using can stay or go — nothing else in the file needs it).

- [ ] **Step 4: Run the full solution test suite to confirm no behavior changed**

Run: `dotnet test`
Expected: PASS — every project, identical results to the end of Plan 2. Purely mechanical move.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor: extract SnagList.Contracts for SnagList.Web to consume"
```

---

## Task 2: Scaffold `SnagList.Web` and OIDC authentication against Keycloak

Adds a second Keycloak client: `snaglist-api` (Plan 1) is confidential and used for
password-grant/service auth; `snaglist-web` is public (a WASM app can't keep a secret) and uses
Authorization Code + PKCE, the standard SPA flow. A client-scope audience mapper makes tokens issued
to `snaglist-web` carry `aud: snaglist-api`, so `SnagList.Api.Auth.Local`'s existing audience check
(Plan 1, Task 15) accepts them without any change on the API side.

**Files:**
- Create: `src/SnagList.Web/SnagList.Web.csproj`
- Create: `src/SnagList.Web/Program.cs`
- Create: `src/SnagList.Web/wwwroot/appsettings.json`
- Modify: `deploy/keycloak/realm-export.json` (Plan 1, Task 22) — add the `snaglist-web` client and
  the `snaglist-api-audience` client scope

**Interfaces:**
- Produces: an authenticated `WebAssemblyHost` — Tasks 3+ add the API client and pages to it.

- [ ] **Step 1: Add the `snaglist-web` client and audience mapper to the realm export**

Add to the top-level of `deploy/keycloak/realm-export.json`, alongside the existing `clients`
array:

```json
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
```

Add a second entry to the existing `clients` array (alongside `snaglist-api` from Plan 1):

```json
    {
      "clientId": "snaglist-web",
      "enabled": true,
      "publicClient": true,
      "protocol": "openid-connect",
      "standardFlowEnabled": true,
      "directAccessGrantsEnabled": false,
      "redirectUris": ["http://localhost:5081/authentication/login-callback", "http://localhost:5081/"],
      "webOrigins": ["http://localhost:5081"],
      "attributes": { "pkce.code.challenge.method": "S256" },
      "defaultClientScopes": ["snaglist-api-audience", "profile", "email", "roles"]
    }
```

- [ ] **Step 2: Scaffold the project**

```bash
dotnet new blazorwasm -o src/SnagList.Web -n SnagList.Web
rm src/SnagList.Web/Pages/Counter.razor src/SnagList.Web/Pages/Weather.razor src/SnagList.Web/Pages/Home.razor
dotnet add src/SnagList.Web package Microsoft.AspNetCore.Components.WebAssembly.Authentication
dotnet add src/SnagList.Web reference src/SnagList.Contracts
dotnet sln add src/SnagList.Web
```

The template's sample pages (`Home`, `Counter`, `Weather`) are deleted immediately — none of them
are this app's UI; Tasks 4+ add real pages from scratch, including a real `"/"` route (Task 5).

- [ ] **Step 3: Write `wwwroot/appsettings.json`**

A browser-based SPA's runtime config only ever needs URLs the *browser* can reach — for local
docker, that's always `localhost:<published-port>`, regardless of the docker-internal service
names `api`/`keycloak` use to reach each other. No runtime environment-variable substitution is
needed the way a server-side app (Plan 1's `api`/`mcp`) requires it.

```json
{
  "Auth": {
    "Authority": "http://localhost:8080/realms/snaglist",
    "ClientId": "snaglist-web",
    "PostLogoutRedirectUri": "http://localhost:5081/"
  },
  "Api": {
    "BaseUrl": "http://localhost:5080/"
  }
}
```

- [ ] **Step 4: Write `Program.cs`**

```csharp
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using SnagList.Web;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddOidcAuthentication(options =>
{
    builder.Configuration.Bind("Auth", options.ProviderOptions);
    options.ProviderOptions.ResponseType = "code";
    options.ProviderOptions.DefaultScopes.Add("snaglist-api-audience");
});

var apiBaseUrl = builder.Configuration["Api:BaseUrl"]
    ?? throw new InvalidOperationException("Api:BaseUrl is required.");

builder.Services.AddHttpClient("SnagListApi", client => client.BaseAddress = new Uri(apiBaseUrl))
    .AddHttpMessageHandler(sp =>
    {
        var handler = sp.GetRequiredService<AuthorizationMessageHandler>();
        handler.ConfigureHandler(authorizedUrls: [apiBaseUrl]);
        return handler;
    });
builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("SnagListApi"));

await builder.Build().RunAsync();
```

- [ ] **Step 5: Write the root `App.razor` and a minimal authenticated layout**

```razor
@* src/SnagList.Web/App.razor *@
<CascadingAuthenticationState>
    <Router AppAssembly="@typeof(App).Assembly">
        <Found Context="routeData">
            <AuthorizeRouteView RouteData="@routeData" DefaultLayout="@typeof(Layout.MainLayout)">
                <NotAuthorized>
                    <RedirectToLogin />
                </NotAuthorized>
            </AuthorizeRouteView>
        </Found>
        <NotFound>
            <p>Page not found.</p>
        </NotFound>
    </Router>
</CascadingAuthenticationState>
```

```razor
@* src/SnagList.Web/Pages/Authentication.razor *@
@page "/authentication/{action}"
<RemoteAuthenticatorView Action="@Action" />

@code {
    [Parameter] public string? Action { get; set; }
}
```

```razor
@* src/SnagList.Web/Shared/RedirectToLogin.razor *@
@inject NavigationManager Navigation
@code {
    protected override void OnInitialized() =>
        Navigation.NavigateTo($"authentication/login?returnUrl={Uri.EscapeDataString(Navigation.Uri)}");
}
```

This plan does not write automated tests for the OIDC redirect/callback flow itself — that needs a
real browser driving a real Keycloak login page, which is exactly what Task 9's manual verification
does instead. Everything downstream of "the user is authenticated" (the API client, the hypermedia
rendering, every page) is fully automatable and gets real tests starting in Task 3.

- [ ] **Step 6: Build to confirm the host compiles**

Run: `dotnet build src/SnagList.Web`
Expected: succeeds.

- [ ] **Step 7: Commit**

```bash
git add src/SnagList.Web deploy/keycloak/realm-export.json SnagList.sln
git commit -m "feat(web): scaffold SnagList.Web with OIDC authentication against Keycloak"
```

---

## Task 3: `SnagListApiClient` — typed HTTP client wrapper

Tested with a fake `HttpMessageHandler`, not a real running API — this is about proving the client
shapes requests and parses responses correctly, the same boundary Plan 1 tested at with fakes for
its own ports. `SnagList.Web` transitively gets `SnagList.Domain`'s enum types (`SnagCategory`,
`SnagSeverity`, `SnagStatus`) through its reference to `SnagList.Contracts` — .NET project
references are transitive for compilation, so no separate reference to `SnagList.Domain` is added.

**Files:**
- Create: `src/SnagList.Web/Services/SnagListApiClient.cs`
- Create: `src/SnagList.Web/Services/SnagListFilter.cs`
- Create: `tests/SnagList.Web.Tests/SnagList.Web.Tests.csproj`
- Test: `tests/SnagList.Web.Tests/Testing/FakeHttpMessageHandler.cs`
- Test: `tests/SnagList.Web.Tests/Services/SnagListApiClientTests.cs`

**Interfaces:**
- Consumes: every contract from Task 1.
- Produces: `SnagListApiClient` — every method Tasks 4–8's pages call against the REST API.

- [ ] **Step 1: Scaffold the test project**

```bash
dotnet new xunit -o tests/SnagList.Web.Tests -n SnagList.Web.Tests
dotnet add tests/SnagList.Web.Tests reference src/SnagList.Web
dotnet sln add tests/SnagList.Web.Tests
```

- [ ] **Step 2: Write the failing tests**

```csharp
namespace SnagList.Web.Tests.Testing;

public sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        return Task.FromResult(respond(request));
    }
}
```

```csharp
namespace SnagList.Web.Tests.Services;

using System.Net;
using System.Net.Http.Json;
using SnagList.Contracts;
using SnagList.Contracts.Snags;
using SnagList.Domain.Snags;
using SnagList.Web.Services;
using SnagList.Web.Tests.Testing;
using Xunit;

public class SnagListApiClientTests
{
    private static SnagListApiClient BuildClient(
        Func<HttpRequestMessage, HttpResponseMessage> respond, out FakeHttpMessageHandler handler)
    {
        handler = new FakeHttpMessageHandler(respond);
        return new SnagListApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") });
    }

    [Fact]
    public async Task ListSnagsAsync_builds_the_expected_query_string()
    {
        var client = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new PagedResponse<SnagSummaryResponse>([], null)),
        }, out var handler);

        await client.ListSnagsAsync(new SnagListFilter(Status: SnagStatus.Reported, Limit: 10), default);

        Assert.Contains("status=Reported", handler.LastRequest!.RequestUri!.Query);
        Assert.Contains("limit=10", handler.LastRequest.RequestUri.Query);
    }

    [Fact]
    public async Task ReportSnagAsync_posts_and_returns_the_new_id()
    {
        var newId = Guid.NewGuid();
        var client = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = JsonContent.Create(new { id = newId }),
        }, out var handler);

        var id = await client.ReportSnagAsync(
            new ReportSnagRequest(Guid.NewGuid(), "3rd floor", SnagCategory.Electrical, SnagSeverity.Medium, "desc"), default);

        Assert.Equal(newId, id);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
    }

    [Fact]
    public async Task EditSnagAsync_sends_a_PATCH_request()
    {
        var client = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.NoContent), out var handler);

        await client.EditSnagAsync(Guid.NewGuid(),
            new EditSnagRequest("4th floor", SnagCategory.Other, SnagSeverity.Low, "desc", 1), default);

        Assert.Equal(HttpMethod.Patch, handler.LastRequest!.Method);
    }

    [Fact]
    public async Task GetLocationAsync_returns_null_on_404_rather_than_throwing()
    {
        var client = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound), out _);

        var result = await client.GetLocationAsync(Guid.NewGuid(), default);

        Assert.Null(result);
    }

    [Fact]
    public async Task UploadSnagPhotoAsync_sends_multipart_form_data()
    {
        var client = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.NoContent), out var handler);
        using var content = new MemoryStream([1, 2, 3]);

        await client.UploadSnagPhotoAsync(Guid.NewGuid(), "light.jpg", "image/jpeg", content, default);

        Assert.IsType<MultipartFormDataContent>(handler.LastRequest!.Content);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/SnagList.Web.Tests --filter SnagListApiClientTests`
Expected: FAIL — `SnagListApiClient`/`SnagListFilter` don't exist.

- [ ] **Step 4: Implement**

```csharp
namespace SnagList.Web.Services;

using SnagList.Domain.Snags;

public sealed record SnagListFilter(
    Guid? LocationId = null, SnagCategory? Category = null, SnagSeverity? Severity = null,
    SnagStatus? Status = null, string? Cursor = null, int Limit = 20)
{
    public string ToQueryString()
    {
        var parts = new List<string> { $"limit={Limit}" };
        if (LocationId is { } locationId) parts.Add($"locationId={locationId}");
        if (Category is { } category) parts.Add($"category={category}");
        if (Severity is { } severity) parts.Add($"severity={severity}");
        if (Status is { } status) parts.Add($"status={status}");
        if (Cursor is { } cursor) parts.Add($"cursor={Uri.EscapeDataString(cursor)}");
        return "?" + string.Join('&', parts);
    }
}
```

```csharp
namespace SnagList.Web.Services;

using System.Net;
using System.Net.Http.Json;
using SnagList.Contracts;
using SnagList.Contracts.Locations;
using SnagList.Contracts.Snags;

public sealed class SnagListApiClient(HttpClient httpClient)
{
    private sealed record CreatedIdResponse(Guid Id);

    public async Task<PagedResponse<LocationResponse>> ListLocationsAsync(bool includeRetired, string? cursor, int limit, CancellationToken ct)
    {
        var query = $"?includeRetired={includeRetired}&limit={limit}" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
        return await httpClient.GetFromJsonAsync<PagedResponse<LocationResponse>>($"api/v1/locations{query}", ct) ?? new([], null);
    }

    public async Task<LocationResponse?> GetLocationAsync(Guid id, CancellationToken ct)
    {
        var response = await httpClient.GetAsync($"api/v1/locations/{id}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LocationResponse>(cancellationToken: ct);
    }

    public async Task<Guid> CreateLocationAsync(CreateLocationRequest request, CancellationToken ct)
    {
        var response = await httpClient.PostAsJsonAsync("api/v1/locations", request, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreatedIdResponse>(cancellationToken: ct))!.Id;
    }

    public async Task UpdateLocationAsync(Guid id, UpdateLocationRequest request, CancellationToken ct) =>
        (await httpClient.PutAsJsonAsync($"api/v1/locations/{id}", request, ct)).EnsureSuccessStatusCode();

    public async Task RetireLocationAsync(Guid id, CancellationToken ct) =>
        (await httpClient.PostAsync($"api/v1/locations/{id}/retire", null, ct)).EnsureSuccessStatusCode();

    public async Task<PagedResponse<SnagSummaryResponse>> ListSnagsAsync(SnagListFilter filter, CancellationToken ct) =>
        await httpClient.GetFromJsonAsync<PagedResponse<SnagSummaryResponse>>($"api/v1/snags{filter.ToQueryString()}", ct)
            ?? new([], null);

    public async Task<SnagDetailResponse?> GetSnagAsync(Guid id, CancellationToken ct)
    {
        var response = await httpClient.GetAsync($"api/v1/snags/{id}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SnagDetailResponse>(cancellationToken: ct);
    }

    public async Task<Guid> ReportSnagAsync(ReportSnagRequest request, CancellationToken ct)
    {
        var response = await httpClient.PostAsJsonAsync("api/v1/snags", request, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreatedIdResponse>(cancellationToken: ct))!.Id;
    }

    public async Task EditSnagAsync(Guid id, EditSnagRequest request, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Patch, $"api/v1/snags/{id}") { Content = JsonContent.Create(request) };
        (await httpClient.SendAsync(message, ct)).EnsureSuccessStatusCode();
    }

    public Task WithdrawSnagAsync(Guid id, int expectedVersion, CancellationToken ct) =>
        PostAsync($"api/v1/snags/{id}/withdraw", new WithdrawSnagRequest(expectedVersion), ct);

    public Task AcknowledgeSnagAsync(Guid id, int expectedVersion, CancellationToken ct) =>
        PostAsync($"api/v1/snags/{id}/acknowledge", new ChangeSnagStatusRequest(expectedVersion), ct);

    public Task StartSnagWorkAsync(Guid id, int expectedVersion, CancellationToken ct) =>
        PostAsync($"api/v1/snags/{id}/start", new ChangeSnagStatusRequest(expectedVersion), ct);

    public Task ResolveSnagAsync(Guid id, int expectedVersion, CancellationToken ct) =>
        PostAsync($"api/v1/snags/{id}/resolve", new ChangeSnagStatusRequest(expectedVersion), ct);

    public Task CloseSnagAsync(Guid id, int expectedVersion, CancellationToken ct) =>
        PostAsync($"api/v1/snags/{id}/close", new ChangeSnagStatusRequest(expectedVersion), ct);

    public Task RejectSnagAsync(Guid id, string reason, int expectedVersion, CancellationToken ct) =>
        PostAsync($"api/v1/snags/{id}/reject", new RejectSnagRequest(reason, expectedVersion), ct);

    public Task AddSnagCommentAsync(Guid id, string body, CancellationToken ct) =>
        PostAsync($"api/v1/snags/{id}/comments", new AddSnagCommentRequest(body), ct);

    public async Task UploadSnagPhotoAsync(Guid id, string fileName, string contentType, Stream content, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        using var streamContent = new StreamContent(content);
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(streamContent, "file", fileName);
        (await httpClient.PostAsync($"api/v1/snags/{id}/photos", form, ct)).EnsureSuccessStatusCode();
    }

    private async Task PostAsync<TRequest>(string url, TRequest request, CancellationToken ct) =>
        (await httpClient.PostAsJsonAsync(url, request, ct)).EnsureSuccessStatusCode();
}
```

- [ ] **Step 5: Register the client and run tests to verify they pass**

Add to `Program.cs` (Task 2): `builder.Services.AddScoped<SnagListApiClient>();`

Run: `dotnet test tests/SnagList.Web.Tests --filter SnagListApiClientTests`
Expected: PASS (5 tests).

- [ ] **Step 6: Commit**

```bash
git add src/SnagList.Web/Services src/SnagList.Web/Program.cs tests/SnagList.Web.Tests SnagList.sln
git commit -m "feat(web): add the typed SnagListApiClient"
```

---

## Task 4: Shared layout — nav, identity display, login/logout

Reuses `SnagList.Authorization`'s `ClaimsPrincipalExtensions` directly (Plan 2, Task 2) rather than
inventing a second claim-reading convention — it's a dependency-free class library, so a WASM app
can reference it exactly like `SnagList.Api`/`SnagList.Mcp` do. **This is display-only.** Reading
`GetStaffRoles()` here to show "you are: Maintenance" in the nav is fine; using it to decide whether
a page's action buttons render is exactly what Task 7 must *not* do — that decision always comes
from a response's `_links`.

**Files:**
- Modify: `src/SnagList.Web/SnagList.Web.csproj` — add project reference to `SnagList.Authorization`
- Create: `src/SnagList.Web/Layout/MainLayout.razor`
- Create: `src/SnagList.Web/Pages/Authentication.razor`
- Create: `src/SnagList.Web/Shared/RedirectToLogin.razor` (already written in Task 2, Step 5 —
  listed here again only because it's used by this layout)

**Interfaces:**
- Consumes: `ClaimsPrincipalExtensions` (Plan 2, Task 2).
- Produces: the app shell every page (Tasks 5–8) renders inside.

- [ ] **Step 1: Add the reference**

```bash
dotnet add src/SnagList.Web reference src/SnagList.Authorization
```

- [ ] **Step 2: Write `MainLayout.razor`**

```razor
@inherits LayoutComponentBase
@using SnagList.Authorization

<div class="page">
    <header>
        <nav>
            <a href="/">Snags</a>
            <a href="/locations">Locations</a>
            <AuthorizeView>
                <Authorized>
                    <span>@context.User.GetStaffName() (@string.Join(", ", context.User.GetStaffRoles()))</span>
                    <a href="authentication/logout">Log out</a>
                </Authorized>
                <NotAuthorized>
                    <a href="authentication/login">Log in</a>
                </NotAuthorized>
            </AuthorizeView>
        </nav>
    </header>
    <main>
        @Body
    </main>
</div>
```

- [ ] **Step 3: Build to confirm it compiles**

Run: `dotnet build src/SnagList.Web`
Expected: succeeds. No automated test for this component — it's pure display markup with no
branching logic of its own to assert on; `AuthorizeView`'s behavior is the framework's, not this
plan's, to verify.

- [ ] **Step 4: Commit**

```bash
git add src/SnagList.Web
git commit -m "feat(web): add the shared layout with identity display and login/logout"
```

---

## Task 5: `Snags` list page — filter and cursor pagination

First `bunit` component test in this plan: register a real `SnagListApiClient` backed by the same
`FakeHttpMessageHandler` from Task 3 into the test's DI container, so the test proves the actual
component-to-client wiring, not a mocked substitute.

**Files:**
- Create: `src/SnagList.Web/Pages/SnagList.razor`
- Test: `tests/SnagList.Web.Tests/Pages/SnagListTests.cs`

**Interfaces:**
- Consumes: `SnagListApiClient.ListSnagsAsync` (Task 3).

- [ ] **Step 1: Add `bunit`**

```bash
dotnet add tests/SnagList.Web.Tests package bunit
```

- [ ] **Step 2: Write the failing test**

```csharp
namespace SnagList.Web.Tests.Pages;

using System.Net;
using System.Net.Http.Json;
using Bunit;
using SnagList.Contracts;
using SnagList.Contracts.Snags;
using SnagList.Web.Pages;
using SnagList.Web.Services;
using SnagList.Web.Tests.Testing;
using Xunit;

public class SnagListTests : TestContext
{
    [Fact]
    public void LoadMore_appends_the_next_page_and_hides_once_NextCursor_is_null()
    {
        var callCount = 0;
        var handler = new FakeHttpMessageHandler(_ =>
        {
            callCount++;
            var page = callCount == 1
                ? new PagedResponse<SnagSummaryResponse>(
                    [new SnagSummaryResponse { Id = Guid.NewGuid(), LocationId = Guid.NewGuid(), SubLocation = "A", Category = default, Severity = default, Status = default, ReportedByName = "Jane", ReportedAt = DateTimeOffset.UtcNow, Version = 1, Links = new Dictionary<string, ApiLink>() }],
                    "cursor-1")
                : new PagedResponse<SnagSummaryResponse>(
                    [new SnagSummaryResponse { Id = Guid.NewGuid(), LocationId = Guid.NewGuid(), SubLocation = "B", Category = default, Severity = default, Status = default, ReportedByName = "Bob", ReportedAt = DateTimeOffset.UtcNow, Version = 1, Links = new Dictionary<string, ApiLink>() }],
                    null);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(page) };
        });
        Services.AddSingleton(new SnagListApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") }));

        var component = RenderComponent<SnagList.Web.Pages.SnagList>();
        Assert.Single(component.FindAll("tbody tr"));
        Assert.NotNull(component.Find("button#load-more"));

        component.Find("button#load-more").Click();

        Assert.Equal(2, component.FindAll("tbody tr").Count);
        Assert.Empty(component.FindAll("button#load-more"));
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/SnagList.Web.Tests --filter SnagListTests`
Expected: FAIL — `SnagList.razor` does not exist.

- [ ] **Step 4: Implement the page**

```razor
@page "/"
@using SnagList.Contracts.Snags
@using SnagList.Domain.Snags
@using SnagList.Web.Services
@inject SnagListApiClient Api
@inject NavigationManager Navigation

<h1>Snags</h1>

<label>
    Status:
    <select @bind="_statusFilter">
        <option value="">(any)</option>
        @foreach (var status in Enum.GetValues<SnagStatus>())
        {
            <option value="@status">@status</option>
        }
    </select>
</label>
<button @onclick="() => LoadAsync(reset: true)">Filter</button>

@if (_items is not null)
{
    <table>
        <thead><tr><th>Sub-location</th><th>Category</th><th>Severity</th><th>Status</th><th>Reported by</th></tr></thead>
        <tbody>
            @foreach (var snag in _items)
            {
                <tr @onclick="() => Navigation.NavigateTo($"/snags/{snag.Id}")">
                    <td>@snag.SubLocation</td><td>@snag.Category</td><td>@snag.Severity</td><td>@snag.Status</td><td>@snag.ReportedByName</td>
                </tr>
            }
        </tbody>
    </table>

    @if (_nextCursor is not null)
    {
        <button id="load-more" @onclick="() => LoadAsync(reset: false)">Load more</button>
    }
}

<a href="/snags/report">Report a Snag</a>

@code {
    private List<SnagSummaryResponse>? _items;
    private string? _nextCursor;
    private string _statusFilter = "";

    protected override Task OnInitializedAsync() => LoadAsync(reset: true);

    private async Task LoadAsync(bool reset)
    {
        var filter = new SnagListFilter(
            Status: string.IsNullOrEmpty(_statusFilter) ? null : Enum.Parse<SnagStatus>(_statusFilter),
            Cursor: reset ? null : _nextCursor);
        var page = await Api.ListSnagsAsync(filter, CancellationToken.None);

        _items = reset ? [.. page.Items] : [.. (_items ?? []), .. page.Items];
        _nextCursor = page.NextCursor;
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/SnagList.Web.Tests --filter SnagListTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/SnagList.Web/Pages/SnagList.razor tests/SnagList.Web.Tests
git commit -m "feat(web): add the Snags list page with filtering and cursor pagination"
```

---

## Task 6: Report Snag page

**Files:**
- Create: `src/SnagList.Web/Pages/ReportSnag.razor`
- Test: `tests/SnagList.Web.Tests/Pages/ReportSnagTests.cs`

**Interfaces:**
- Consumes: `SnagListApiClient.ListLocationsAsync`, `.ReportSnagAsync` (Task 3).

- [ ] **Step 1: Write the failing test**

```csharp
namespace SnagList.Web.Tests.Pages;

using System.Net;
using System.Net.Http.Json;
using Bunit;
using SnagList.Contracts;
using SnagList.Contracts.Locations;
using SnagList.Web.Services;
using SnagList.Web.Tests.Testing;
using Xunit;

public class ReportSnagTests : TestContext
{
    [Fact]
    public void Submitting_the_form_posts_the_report_and_navigates_to_the_new_Snag()
    {
        var locationId = Guid.NewGuid();
        var newSnagId = Guid.NewGuid();
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new PagedResponse<LocationResponse>(
                        [new LocationResponse { Id = locationId, Name = "Head Office", Address = "1 Main St", IsActive = true, Links = new() }],
                        null)),
                };
            }
            return new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(new { id = newSnagId }) };
        });
        Services.AddSingleton(new SnagListApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") }));
        var navigationManager = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();

        var component = RenderComponent<SnagList.Web.Pages.ReportSnag>();
        component.Find("textarea#description").Change("Flickering light");
        component.Find("form").Submit();

        Assert.EndsWith($"/snags/{newSnagId}", navigationManager.Uri);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/SnagList.Web.Tests --filter ReportSnagTests`
Expected: FAIL — `ReportSnag.razor` does not exist.

- [ ] **Step 3: Implement the page**

```razor
@page "/snags/report"
@using SnagList.Contracts.Locations
@using SnagList.Contracts.Snags
@using SnagList.Domain.Snags
@using SnagList.Web.Services
@inject SnagListApiClient Api
@inject NavigationManager Navigation

<h1>Report a Snag</h1>

<EditForm Model="_model" OnSubmit="SubmitAsync">
    <label>
        Location:
        <select @bind="_model.LocationId">
            @foreach (var location in _locations)
            {
                <option value="@location.Id">@location.Name</option>
            }
        </select>
    </label>
    <label>Sub-location: <InputText @bind-Value="_model.SubLocation" /></label>
    <label>
        Category:
        <select @bind="_model.Category">
            @foreach (var category in Enum.GetValues<SnagCategory>())
            {
                <option value="@category">@category</option>
            }
        </select>
    </label>
    <label>
        Severity:
        <select @bind="_model.Severity">
            @foreach (var severity in Enum.GetValues<SnagSeverity>())
            {
                <option value="@severity">@severity</option>
            }
        </select>
    </label>
    <label>Description: <InputTextArea id="description" @bind-Value="_model.Description" /></label>
    <button type="submit">Report</button>
</EditForm>

@code {
    private readonly FormModel _model = new();
    private List<LocationResponse> _locations = [];

    protected override async Task OnInitializedAsync()
    {
        var page = await Api.ListLocationsAsync(includeRetired: false, cursor: null, limit: 100, CancellationToken.None);
        _locations = [.. page.Items];
        if (_locations.Count > 0) _model.LocationId = _locations[0].Id;
    }

    private async Task SubmitAsync()
    {
        var id = await Api.ReportSnagAsync(
            new ReportSnagRequest(_model.LocationId, _model.SubLocation, _model.Category, _model.Severity, _model.Description),
            CancellationToken.None);
        Navigation.NavigateTo($"/snags/{id}");
    }

    private sealed class FormModel
    {
        public Guid LocationId { get; set; }
        public string SubLocation { get; set; } = "";
        public SnagCategory Category { get; set; }
        public SnagSeverity Severity { get; set; }
        public string Description { get; set; } = "";
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/SnagList.Web.Tests --filter ReportSnagTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SnagList.Web/Pages/ReportSnag.razor tests/SnagList.Web.Tests
git commit -m "feat(web): add the Report Snag page"
```

---

## Task 7: `SnagActionButtons` and the Snag detail page — the HATEOAS payoff, made visible

The one component this whole plan exists to prove out: it renders a button **only because a key is
present in `Links`**, never because of a client-side re-derivation of "is this the reporter" or "is
this Maintenance" or "what status is this." `SnagLinksBuilder` (Plan 1, Task 16) already decided
all of that server-side; this component's entire job is to not decide it again.

**Files:**
- Create: `src/SnagList.Web/Shared/SnagActionButtons.razor`
- Create: `src/SnagList.Web/Pages/SnagDetail.razor`
- Test: `tests/SnagList.Web.Tests/Shared/SnagActionButtonsTests.cs`
- Test: `tests/SnagList.Web.Tests/Pages/SnagDetailTests.cs`

**Interfaces:**
- Consumes: `SnagListApiClient` (Task 3) — every transition/comment/photo method.
- Produces: `SnagActionButtons` — a reusable, independently-tested unit; nothing else in this plan
  consumes it, but it's the component a reviewer should be able to trust without re-reading
  `SnagDetail.razor`'s wiring around it.

- [ ] **Step 1: Write the failing `SnagActionButtons` tests**

```csharp
namespace SnagList.Web.Tests.Shared;

using Bunit;
using Microsoft.AspNetCore.Components;
using SnagList.Contracts;
using SnagList.Web.Shared;
using Xunit;

public class SnagActionButtonsTests : TestContext
{
    [Fact]
    public void Renders_edit_and_withdraw_when_present_in_Links_and_nothing_else()
    {
        var links = new Dictionary<string, ApiLink>
        {
            ["self"] = new("/x", "GET", "GetSnag"),
            ["edit"] = new("/x", "PATCH", "EditSnag"),
            ["withdraw"] = new("/x/withdraw", "POST", "WithdrawSnag"),
        };

        var component = RenderComponent<SnagActionButtons>(p => p.Add(x => x.Links, links));

        Assert.NotEmpty(component.FindAll("#action-edit"));
        Assert.NotEmpty(component.FindAll("#action-withdraw"));
        Assert.Empty(component.FindAll("#action-acknowledge"));
        Assert.Empty(component.FindAll("#action-resolve"));
        Assert.Empty(component.FindAll("#action-close"));
        Assert.Empty(component.FindAll("#action-reject"));
    }

    [Fact]
    public void Renders_only_close_when_that_is_the_only_transition_link_present()
    {
        var links = new Dictionary<string, ApiLink>
        {
            ["self"] = new("/x", "GET", "GetSnag"),
            ["close"] = new("/x/close", "POST", "CloseSnag"),
        };

        var component = RenderComponent<SnagActionButtons>(p => p.Add(x => x.Links, links));

        Assert.NotEmpty(component.FindAll("#action-close"));
        Assert.Empty(component.FindAll("#action-acknowledge"));
        Assert.Empty(component.FindAll("#action-resolve"));
        Assert.Empty(component.FindAll("#action-edit"));
    }

    [Fact]
    public void Renders_no_transition_buttons_when_Links_carries_only_self_comments_and_photos()
    {
        var links = new Dictionary<string, ApiLink>
        {
            ["self"] = new("/x", "GET", "GetSnag"),
            ["comments"] = new("/x/comments", "POST", "AddSnagComment"),
            ["photos"] = new("/x/photos", "POST", "UploadSnagPhoto"),
        };

        var component = RenderComponent<SnagActionButtons>(p => p.Add(x => x.Links, links));

        Assert.Empty(component.FindAll(".snag-actions button"));
    }

    [Fact]
    public void Clicking_Acknowledge_invokes_OnAcknowledge()
    {
        var invoked = false;
        var links = new Dictionary<string, ApiLink> { ["acknowledge"] = new("/x/acknowledge", "POST", "AcknowledgeSnag") };

        var component = RenderComponent<SnagActionButtons>(p => p
            .Add(x => x.Links, links)
            .Add(x => x.OnAcknowledge, EventCallback.Factory.Create(this, () => invoked = true)));

        component.Find("#action-acknowledge").Click();

        Assert.True(invoked);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/SnagList.Web.Tests --filter SnagActionButtonsTests`
Expected: FAIL — `SnagActionButtons` does not exist.

- [ ] **Step 3: Implement `SnagActionButtons`**

```razor
@using SnagList.Contracts

<div class="snag-actions">
    @if (Links.ContainsKey("edit"))
    {
        <button id="action-edit" @onclick="OnEdit">Edit</button>
    }
    @if (Links.ContainsKey("withdraw"))
    {
        <button id="action-withdraw" @onclick="OnWithdraw">Withdraw</button>
    }
    @if (Links.ContainsKey("acknowledge"))
    {
        <button id="action-acknowledge" @onclick="OnAcknowledge">Acknowledge</button>
    }
    @if (Links.ContainsKey("start"))
    {
        <button id="action-start" @onclick="OnStart">Start work</button>
    }
    @if (Links.ContainsKey("resolve"))
    {
        <button id="action-resolve" @onclick="OnResolve">Resolve</button>
    }
    @if (Links.ContainsKey("close"))
    {
        <button id="action-close" @onclick="OnClose">Close</button>
    }
    @if (Links.ContainsKey("reject"))
    {
        <button id="action-reject" @onclick="OnReject">Reject</button>
    }
</div>

@code {
    [Parameter, EditorRequired] public IReadOnlyDictionary<string, ApiLink> Links { get; set; } = null!;
    [Parameter] public EventCallback OnEdit { get; set; }
    [Parameter] public EventCallback OnWithdraw { get; set; }
    [Parameter] public EventCallback OnAcknowledge { get; set; }
    [Parameter] public EventCallback OnStart { get; set; }
    [Parameter] public EventCallback OnResolve { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }
    [Parameter] public EventCallback OnReject { get; set; }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/SnagList.Web.Tests --filter SnagActionButtonsTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Write the failing `SnagDetail` page test**

```csharp
namespace SnagList.Web.Tests.Pages;

using System.Net;
using System.Net.Http.Json;
using Bunit;
using SnagList.Contracts;
using SnagList.Contracts.Snags;
using SnagList.Domain.Snags;
using SnagList.Web.Services;
using SnagList.Web.Tests.Testing;
using Xunit;

public class SnagDetailTests : TestContext
{
    private static SnagDetailResponse Detail(
        Guid id, IReadOnlyDictionary<string, ApiLink> links, SnagStatus status = SnagStatus.Reported) => new()
    {
        Id = id, LocationId = Guid.NewGuid(), SubLocation = "3rd floor", Category = SnagCategory.Electrical,
        Severity = SnagSeverity.Medium, Description = "Flickering light", Status = status,
        ReportedByStaffId = "U1", ReportedByName = "Jane", ReportedAt = DateTimeOffset.UtcNow, Version = 1,
        Comments = [], Photos = [], Links = links,
    };

    [Fact]
    public void Clicking_Acknowledge_calls_the_API_and_reloads_the_updated_Snag()
    {
        var snagId = Guid.NewGuid();
        var reportedLinks = new Dictionary<string, ApiLink> { ["acknowledge"] = new($"/api/v1/snags/{snagId}/acknowledge", "POST", "AcknowledgeSnag") };
        var acknowledgedLinks = new Dictionary<string, ApiLink> { ["resolve"] = new($"/api/v1/snags/{snagId}/resolve", "POST", "ResolveSnag") };
        var getCallCount = 0;
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Get)
            {
                getCallCount++;
                var detail = getCallCount == 1
                    ? Detail(snagId, reportedLinks)
                    : Detail(snagId, acknowledgedLinks, SnagStatus.Acknowledged);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(detail) };
            }
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        Services.AddSingleton(new SnagListApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") }));

        var component = RenderComponent<SnagList.Web.Pages.SnagDetail>(p => p.Add(x => x.SnagId, snagId));
        Assert.NotEmpty(component.FindAll("#action-acknowledge"));
        Assert.Empty(component.FindAll("#action-resolve"));

        component.Find("#action-acknowledge").Click();

        Assert.NotEmpty(component.FindAll("#action-resolve"));
        Assert.Empty(component.FindAll("#action-acknowledge"));
    }
}
```

- [ ] **Step 6: Run test to verify it fails**

Run: `dotnet test tests/SnagList.Web.Tests --filter SnagDetailTests`
Expected: FAIL — `SnagDetail.razor` does not exist.

- [ ] **Step 7: Implement the page**

```razor
@page "/snags/{SnagId:guid}"
@using SnagList.Contracts.Snags
@using SnagList.Domain.Snags
@using SnagList.Web.Services
@using SnagList.Web.Shared
@inject SnagListApiClient Api

@if (_snag is not null)
{
    <h1>@_snag.SubLocation</h1>
    <p>Status: @_snag.Status | Category: @_snag.Category | Severity: @_snag.Severity</p>
    <p>@_snag.Description</p>

    <SnagActionButtons Links="_snag.Links"
        OnEdit="BeginEdit"
        OnWithdraw="() => TransitionAsync((id, v, ct) => Api.WithdrawSnagAsync(id, v, ct))"
        OnAcknowledge="() => TransitionAsync(Api.AcknowledgeSnagAsync)"
        OnStart="() => TransitionAsync(Api.StartSnagWorkAsync)"
        OnResolve="() => TransitionAsync(Api.ResolveSnagAsync)"
        OnClose="() => TransitionAsync(Api.CloseSnagAsync)"
        OnReject="() => _rejecting = true" />

    @if (_editing)
    {
        <EditForm Model="_editModel" OnValidSubmit="EditAsync">
            <InputText @bind-Value="_editModel.SubLocation" />
            <select @bind="_editModel.Category">
                @foreach (var category in Enum.GetValues<SnagCategory>())
                {
                    <option value="@category">@category</option>
                }
            </select>
            <select @bind="_editModel.Severity">
                @foreach (var severity in Enum.GetValues<SnagSeverity>())
                {
                    <option value="@severity">@severity</option>
                }
            </select>
            <InputTextArea @bind-Value="_editModel.Description" />
            <button type="submit">Save</button>
        </EditForm>
    }

    @if (_rejecting)
    {
        <EditForm Model="_rejectModel" OnValidSubmit="RejectAsync">
            <InputTextArea @bind-Value="_rejectModel.Reason" placeholder="Reason" />
            <button type="submit">Confirm reject</button>
        </EditForm>
    }

    <h2>Comments</h2>
    @foreach (var comment in _snag.Comments)
    {
        <p><strong>@comment.AuthorName</strong>: @comment.Body</p>
    }
    <EditForm Model="_commentModel" OnValidSubmit="AddCommentAsync">
        <InputTextArea @bind-Value="_commentModel.Body" />
        <button type="submit">Add comment</button>
    </EditForm>

    <h2>Photos</h2>
    @foreach (var photo in _snag.Photos)
    {
        <a href="@photo.Href.Href" target="_blank">@photo.FileName</a>
    }
    <InputFile OnChange="UploadPhotoAsync" />
}

@code {
    [Parameter] public Guid SnagId { get; set; }
    private SnagDetailResponse? _snag;
    private bool _editing;
    private bool _rejecting;
    private readonly CommentModel _commentModel = new();
    private readonly EditModel _editModel = new();
    private readonly RejectModel _rejectModel = new();

    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task LoadAsync() => _snag = await Api.GetSnagAsync(SnagId, CancellationToken.None);

    private void BeginEdit()
    {
        _editModel.SubLocation = _snag!.SubLocation;
        _editModel.Category = _snag.Category;
        _editModel.Severity = _snag.Severity;
        _editModel.Description = _snag.Description;
        _editing = true;
    }

    private async Task EditAsync()
    {
        await Api.EditSnagAsync(SnagId,
            new EditSnagRequest(_editModel.SubLocation, _editModel.Category, _editModel.Severity, _editModel.Description, _snag!.Version),
            CancellationToken.None);
        _editing = false;
        await LoadAsync();
    }

    private async Task TransitionAsync(Func<Guid, int, CancellationToken, Task> action)
    {
        await action(SnagId, _snag!.Version, CancellationToken.None);
        await LoadAsync();
    }

    private async Task RejectAsync()
    {
        await Api.RejectSnagAsync(SnagId, _rejectModel.Reason, _snag!.Version, CancellationToken.None);
        _rejecting = false;
        await LoadAsync();
    }

    private async Task AddCommentAsync()
    {
        await Api.AddSnagCommentAsync(SnagId, _commentModel.Body, CancellationToken.None);
        _commentModel.Body = "";
        await LoadAsync();
    }

    private async Task UploadPhotoAsync(Microsoft.AspNetCore.Components.Forms.InputFileChangeEventArgs e)
    {
        await using var stream = e.File.OpenReadStream(maxAllowedSize: 10_000_000);
        await Api.UploadSnagPhotoAsync(SnagId, e.File.Name, e.File.ContentType, stream, CancellationToken.None);
        await LoadAsync();
    }

    private sealed class CommentModel { public string Body { get; set; } = ""; }
    private sealed class RejectModel { public string Reason { get; set; } = ""; }

    private sealed class EditModel
    {
        public string SubLocation { get; set; } = "";
        public SnagCategory Category { get; set; }
        public SnagSeverity Severity { get; set; }
        public string Description { get; set; } = "";
    }
}
```

Note `TransitionAsync`'s edit/withdraw call in `OnWithdraw` is wrapped in a lambda rather than
passed directly, since `WithdrawSnagAsync`'s signature is identical to the status-transition
methods' but isn't itself one — this keeps `TransitionAsync`'s single `Func<Guid, int,
CancellationToken, Task>` shape reusable across all five without a separate near-duplicate method.

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test tests/SnagList.Web.Tests`
Expected: PASS — every Web test from Tasks 3–7.

- [ ] **Step 9: Commit**

```bash
git add src/SnagList.Web/Shared/SnagActionButtons.razor src/SnagList.Web/Pages/SnagDetail.razor tests/SnagList.Web.Tests
git commit -m "feat(web): add SnagActionButtons (hypermedia-driven) and the Snag detail page"
```

---

## Task 8: `Locations` management page

**Design note — a real gap this page's design surfaced:** `PagedResponse<T>` (Plan 1, Task 16) has
no collection-level `_links`, only each item does (per `LocationLinksBuilder`, Plan 1, Task 17) —
so there's no hypermedia signal at all for "can I create a new one," especially when the list is
empty. Rather than invent an inference (e.g. "if any item has an `update` link, assume create is
allowed too" — which is exactly the kind of guessing this plan's Global Constraints rule out), the
page always offers the "New Location" button and surfaces the server's 403 if the caller turns out
not to be `Maintenance`. That's more honest than a client-side guess, given the API doesn't
currently expose a collection-level link to check instead.

**Files:**
- Create: `src/SnagList.Web/Pages/Locations.razor`
- Test: `tests/SnagList.Web.Tests/Pages/LocationsTests.cs`

**Interfaces:**
- Consumes: `SnagListApiClient.ListLocationsAsync`, `.CreateLocationAsync`, `.RetireLocationAsync`
  (Task 3).

- [ ] **Step 1: Write the failing tests**

```csharp
namespace SnagList.Web.Tests.Pages;

using System.Net;
using System.Net.Http.Json;
using Bunit;
using SnagList.Contracts;
using SnagList.Contracts.Locations;
using SnagList.Web.Services;
using SnagList.Web.Tests.Testing;
using Xunit;

public class LocationsTests : TestContext
{
    [Fact]
    public void Retire_button_only_appears_for_a_Location_whose_Links_carry_it()
    {
        var withRetire = new LocationResponse
        {
            Id = Guid.NewGuid(), Name = "Head Office", Address = "1 Main St", IsActive = true,
            Links = new Dictionary<string, ApiLink> { ["retire"] = new("/x", "POST", "RetireLocation") },
        };
        var withoutRetire = new LocationResponse
        {
            Id = Guid.NewGuid(), Name = "Old Depot", Address = "9 Yard Ln", IsActive = false,
            Links = new Dictionary<string, ApiLink>(),
        };
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new PagedResponse<LocationResponse>([withRetire, withoutRetire], null)),
        });
        Services.AddSingleton(new SnagListApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") }));

        var component = RenderComponent<SnagList.Web.Pages.Locations>();

        Assert.Single(component.FindAll("button.retire"));
    }

    [Fact]
    public void Creating_a_Location_shows_a_message_on_403_rather_than_throwing()
    {
        var handler = new FakeHttpMessageHandler(req => req.Method == HttpMethod.Get
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new PagedResponse<LocationResponse>([], null)) }
            : new HttpResponseMessage(HttpStatusCode.Forbidden));
        Services.AddSingleton(new SnagListApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") }));

        var component = RenderComponent<SnagList.Web.Pages.Locations>();
        component.Find("button#new-location").Click();
        component.Find("input#name").Change("Test Site");
        component.Find("input#address").Change("1 Test St");
        component.Find("form").Submit();

        Assert.Contains("Maintenance role", component.Markup);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/SnagList.Web.Tests --filter LocationsTests`
Expected: FAIL — `Locations.razor` does not exist.

- [ ] **Step 3: Implement the page**

```razor
@page "/locations"
@using System.Net
@using SnagList.Contracts.Locations
@using SnagList.Web.Services
@inject SnagListApiClient Api

<h1>Locations</h1>

<table>
    <thead><tr><th>Name</th><th>Address</th><th>Active</th><th></th></tr></thead>
    <tbody>
        @foreach (var location in _locations)
        {
            <tr>
                <td>@location.Name</td>
                <td>@location.Address</td>
                <td>@location.IsActive</td>
                <td>
                    @if (location.Links.ContainsKey("retire"))
                    {
                        <button class="retire" @onclick="() => RetireAsync(location.Id)">Retire</button>
                    }
                </td>
            </tr>
        }
    </tbody>
</table>

@if (_creating)
{
    <EditForm Model="_createModel" OnValidSubmit="CreateAsync">
        <input id="name" @bind="_createModel.Name" placeholder="Name" />
        <input id="address" @bind="_createModel.Address" placeholder="Address" />
        <button type="submit">Create</button>
    </EditForm>
    @if (_createError is not null)
    {
        <p class="error">@_createError</p>
    }
}
else
{
    <button id="new-location" @onclick="() => _creating = true">New Location</button>
}

@code {
    private List<LocationResponse> _locations = [];
    private bool _creating;
    private string? _createError;
    private readonly CreateModel _createModel = new();

    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        var page = await Api.ListLocationsAsync(includeRetired: true, cursor: null, limit: 100, CancellationToken.None);
        _locations = [.. page.Items];
    }

    private async Task CreateAsync()
    {
        try
        {
            await Api.CreateLocationAsync(new CreateLocationRequest(_createModel.Name, _createModel.Address), CancellationToken.None);
            _creating = false;
            _createError = null;
            await LoadAsync();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            _createError = "You need the Maintenance role to create a Location.";
        }
    }

    private async Task RetireAsync(Guid id)
    {
        await Api.RetireLocationAsync(id, CancellationToken.None);
        await LoadAsync();
    }

    private sealed class CreateModel
    {
        public string Name { get; set; } = "";
        public string Address { get; set; } = "";
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/SnagList.Web.Tests --filter LocationsTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Run the full Web test suite, then commit**

Run: `dotnet test tests/SnagList.Web.Tests`
Expected: PASS — every test from Tasks 3–8.

```bash
git add src/SnagList.Web/Pages/Locations.razor tests/SnagList.Web.Tests
git commit -m "feat(web): add the Locations management page"
```

---

## Task 9: Add `web` to the local docker deployment

A Blazor WASM standalone app publishes to static files — no .NET runtime needed to serve it, just a
static file server that also falls back to `index.html` for client-side routes.

**Files:**
- Create: `src/SnagList.Web/nginx.conf`
- Create: `src/SnagList.Web/Dockerfile`
- Modify: `docker-compose.yml` (Plan 1, Task 22) — add the `web` service

**Interfaces:** none — deployment wiring for everything Tasks 1–8 built.

- [ ] **Step 1: Write the nginx config**

```nginx
server {
    listen 8080;
    root /usr/share/nginx/html;
    include /etc/nginx/mime.types;
    types { application/wasm wasm; }

    location / {
        try_files $uri $uri/ /index.html;
    }
}
```

- [ ] **Step 2: Write the Dockerfile**

```dockerfile
# src/SnagList.Web/Dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/SnagList.Web/SnagList.Web.csproj -c Release -o /app

FROM nginx:alpine AS runtime
COPY --from=build /app/wwwroot /usr/share/nginx/html
COPY src/SnagList.Web/nginx.conf /etc/nginx/conf.d/default.conf
```

- [ ] **Step 3: Add the `web` service to `docker-compose.yml`**

```yaml
  web:
    build:
      context: .
      dockerfile: src/SnagList.Web/Dockerfile
    ports:
      - "5081:8080"
```

No `depends_on` — `web` only serves static files; the browser talks to `keycloak`/`api` directly
over their own published ports once the page loads, so `web`'s own container has no runtime
dependency on either being up first.

- [ ] **Step 4: Bring the stack up and manually verify the full browser flow**

```bash
docker compose up -d --build
```

In a browser:

1. Open `http://localhost:5081`. Expected: redirected to Keycloak's login page.
2. Log in as `jane.smith` / `password` (Plan 1, Task 22's seeded demo user — `Staff` only).
3. Expected: redirected back to the Snags list, nav shows "Jane Smith (Staff)".
4. Click "Report a Snag", fill the form, submit. Expected: redirected to the new Snag's detail
   page, showing `Edit` and `Withdraw` buttons (the reporter, while `Reported`) but no
   `Acknowledge`/`Resolve`/`Close`.
5. Log out, log back in as `bob.maintenance` / `password` (`Staff` + `Maintenance`). Open the same
   Snag. Expected: `Acknowledge` and `Reject` buttons instead — no `Edit`/`Withdraw` (not the
   reporter).
6. Click `Acknowledge`. Expected: the button set changes to `Start work`/`Reject` without a page
   reload, matching Task 7's automated test but now against the real running stack.
7. Upload a photo; confirm it appears in the Photos list and its link opens the image.

Expected: every step matches, confirming the full path — Keycloak login → token → API call →
hypermedia-driven UI — works end to end, not just against `bunit` and fakes.

- [ ] **Step 5: Commit**

```bash
git add src/SnagList.Web/nginx.conf src/SnagList.Web/Dockerfile docker-compose.yml
git commit -m "feat(deploy): add web service to the local docker deployment"
```

---

## Plan exit criteria

- `dotnet test` (every project, including Plans 1–2's) passes.
- `docker compose up -d --build && docker compose --profile seed run --rm seed` brings up `api`,
  `mcp`, `web`, and every dependency together.
- Task 9's manual browser verification succeeds against the real stack — the reporter sees
  `Edit`/`Withdraw` and no maintenance actions; a `Maintenance` caller sees the opposite; every
  button set changes only in response to the server's `_links`, never a client-side role/status
  check.
- No new domain concepts were introduced — this plan is pure presentation-layer wiring, so
  `docs/ontology.ttl` needs no changes.

**Not in this plan** (remaining follow-on plans): the home-lab deployment and the AWS deployment.
