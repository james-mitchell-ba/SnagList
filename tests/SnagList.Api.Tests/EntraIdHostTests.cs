namespace SnagList.Api.Tests;

using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

public class EntraIdHostTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Host_boots_with_Auth_Provider_set_to_EntraId()
    {
        var client = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:SnagList", "Host=localhost;Database=x;Username=x;Password=x");
            builder.UseSetting("Storage:BucketName", "test-bucket");
            builder.UseSetting("Storage:Provider", "S3");
            builder.UseSetting("Email:Provider", "Ses");
            builder.UseSetting("Email:FromAddress", "snaglist@example.com");
            builder.UseSetting("Notifications:MaintenanceTeamEmail", "maintenance@example.com");
            builder.UseSetting("Auth:Provider", "EntraId");
            builder.UseSetting("Auth:EntraId:TenantId", "11111111-1111-1111-1111-111111111111");
            builder.UseSetting("Auth:EntraId:Audience", "snaglist-api");
        }).CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
