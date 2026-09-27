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
