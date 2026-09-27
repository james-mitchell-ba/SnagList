namespace SnagList.Mcp.Tests.Tools;

using Microsoft.Extensions.DependencyInjection;
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
        Guid snagId = reported.Id;

        using var editScope = fixture.CreateScope();
        var editTools = fixture.CreateTool<SnagTools>(editScope, "U100051", "Staff");
        await editTools.EditSnag(snagId, "4th floor, room 4.01", SnagCategory.Other, SnagSeverity.Low, "desc, more detail", 1, default);

        using var withdrawScope = fixture.CreateScope();
        var withdrawTools = fixture.CreateTool<SnagTools>(withdrawScope, "U100051", "Staff");
        await withdrawTools.WithdrawSnag(snagId, 2, default);
    }
}
