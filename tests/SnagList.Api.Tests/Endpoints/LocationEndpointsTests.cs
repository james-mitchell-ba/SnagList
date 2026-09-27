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
