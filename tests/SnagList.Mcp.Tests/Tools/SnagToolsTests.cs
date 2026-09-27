namespace SnagList.Mcp.Tests.Tools;

using Microsoft.Extensions.DependencyInjection;
using SnagList.Application.Locations.Commands;
using SnagList.Application.Snags.Queries;
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

    [Fact]
    public async Task Maintenance_can_drive_a_Snag_through_acknowledge_start_resolve_close()
    {
        var locationId = await CreateLocationAsync();
        using var reportScope = fixture.CreateScope();
        dynamic reported = await fixture.CreateTool<SnagTools>(reportScope, "U100052", "Staff")
            .ReportSnag(locationId, "5th floor", SnagCategory.Other, SnagSeverity.Low, "desc", default);
        Guid snagId = reported.Id;

        using var scope1 = fixture.CreateScope();
        await fixture.CreateTool<SnagTools>(scope1, "U900050", "Maintenance").AcknowledgeSnag(snagId, 1, default);
        using var scope2 = fixture.CreateScope();
        await fixture.CreateTool<SnagTools>(scope2, "U900050", "Maintenance").StartSnagWork(snagId, 2, default);
        using var scope3 = fixture.CreateScope();
        await fixture.CreateTool<SnagTools>(scope3, "U900050", "Maintenance").ResolveSnag(snagId, 3, default);
        using var scope4 = fixture.CreateScope();
        await fixture.CreateTool<SnagTools>(scope4, "U900050", "Maintenance").CloseSnag(snagId, 4, default);
    }

    [Fact]
    public async Task Staff_calling_AcknowledgeSnag_is_rejected()
    {
        var locationId = await CreateLocationAsync();
        using var reportScope = fixture.CreateScope();
        dynamic reported = await fixture.CreateTool<SnagTools>(reportScope, "U100053", "Staff")
            .ReportSnag(locationId, "6th floor", SnagCategory.Other, SnagSeverity.Low, "desc", default);
        Guid snagId = reported.Id;

        using var scope = fixture.CreateScope();
        var tools = fixture.CreateTool<SnagTools>(scope, "U100053", "Staff");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.AcknowledgeSnag(snagId, 1, default));
    }

    [Fact]
    public async Task RejectSnag_records_the_reason_as_a_comment()
    {
        var locationId = await CreateLocationAsync();
        using var reportScope = fixture.CreateScope();
        dynamic reported = await fixture.CreateTool<SnagTools>(reportScope, "U100054", "Staff")
            .ReportSnag(locationId, "7th floor", SnagCategory.Other, SnagSeverity.Low, "desc", default);
        Guid snagId = reported.Id;

        using var scope = fixture.CreateScope();
        await fixture.CreateTool<SnagTools>(scope, "U900051", "Maintenance").RejectSnag(snagId, "Duplicate", 1, default);
    }

    [Fact]
    public async Task AddSnagComment_succeeds_for_either_role()
    {
        var locationId = await CreateLocationAsync();
        using var reportScope = fixture.CreateScope();
        dynamic reported = await fixture.CreateTool<SnagTools>(reportScope, "U100055", "Staff")
            .ReportSnag(locationId, "8th floor", SnagCategory.Other, SnagSeverity.Low, "desc", default);
        Guid snagId = reported.Id;

        using var scope = fixture.CreateScope();
        await fixture.CreateTool<SnagTools>(scope, "U100055", "Staff").AddSnagComment(snagId, "Any update?", default);
    }

    [Fact]
    public async Task UploadSnagPhoto_then_GetSnagPhoto_returns_a_url()
    {
        var locationId = await CreateLocationAsync();
        using var reportScope = fixture.CreateScope();
        dynamic reported = await fixture.CreateTool<SnagTools>(reportScope, "U100056", "Staff")
            .ReportSnag(locationId, "9th floor", SnagCategory.Other, SnagSeverity.Low, "desc", default);
        Guid snagId = reported.Id;
        var base64 = Convert.ToBase64String([1, 2, 3, 4]);

        using var uploadScope = fixture.CreateScope();
        await fixture.CreateTool<SnagTools>(uploadScope, "U100056", "Staff")
            .UploadSnagPhoto(snagId, "light.jpg", "image/jpeg", base64, default);

        using var getScope = fixture.CreateScope();
        var detail = await getScope.ServiceProvider.GetRequiredService<GetSnagQueryHandler>()
            .HandleAsync(new SnagList.Application.Snags.Queries.GetSnagQuery(snagId), default);
        var photoKey = detail!.Photos[0].BlobKey[$"snags/{snagId}/".Length..];

        using var downloadScope = fixture.CreateScope();
        dynamic result = await fixture.CreateTool<SnagTools>(downloadScope, "U100056", "Staff")
            .GetSnagPhoto(snagId, photoKey, default);

        Assert.Contains(photoKey, (string)result.Url);
    }
}
