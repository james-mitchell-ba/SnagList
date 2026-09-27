namespace SnagList.Mcp.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public class ToolHostingTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public void Tool_identity_services_resolve_from_a_request_scope()
    {
        using var scope = factory.WithWebHostBuilder(builder =>
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
        }).Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>());
    }
}
