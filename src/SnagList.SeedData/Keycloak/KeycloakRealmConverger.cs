namespace SnagList.SeedData.Keycloak;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class KeycloakRealmConverger(HttpClient httpClient, KeycloakAdminOptions options)
{
    private sealed record TokenResponse([property: JsonPropertyName("access_token")] string AccessToken);

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
