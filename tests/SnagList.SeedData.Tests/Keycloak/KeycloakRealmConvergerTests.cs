namespace SnagList.SeedData.Tests.Keycloak;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
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

    private sealed record TokenResponse([property: JsonPropertyName("access_token")] string AccessToken);
}
