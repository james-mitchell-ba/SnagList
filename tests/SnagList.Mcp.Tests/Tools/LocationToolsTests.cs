namespace SnagList.Mcp.Tests.Tools;

using Microsoft.Extensions.DependencyInjection;
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

        Assert.NotEmpty((IEnumerable<object>)page.Items);
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
