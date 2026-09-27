namespace SnagList.Mcp.Tools;

using System.ComponentModel;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;
using SnagList.Application.Snags.Commands;
using SnagList.Authorization;
using SnagList.Domain.Snags;

public sealed record ReportSnagResult(Guid Id);

[McpServerToolType]
public sealed class SnagTools(
    IHttpContextAccessor httpContextAccessor,
    ReportSnagCommandHandler reportHandler,
    EditSnagCommandHandler editHandler,
    WithdrawSnagCommandHandler withdrawHandler)
{
    private System.Security.Claims.ClaimsPrincipal User => httpContextAccessor.HttpContext!.User;

    [McpServerTool(Name = "report_snag")]
    [Description("Reports a new building-quality or maintenance issue (a Snag) at a Location.")]
    public async Task<ReportSnagResult> ReportSnag(
        [Description("The id of the Location this Snag is being reported at.")] Guid locationId,
        [Description("Free-text sub-location, e.g. '3rd floor, room 3.12'.")] string subLocation,
        [Description("Electrical, Plumbing, StructuralOrFabric, HeatingAndCooling, CleaningAndHousekeeping, SafetyHazard, or Other.")] SnagCategory category,
        [Description("Low, Medium, High, or SafetyCritical.")] SnagSeverity severity,
        [Description("Description of the issue.")] string description,
        CancellationToken ct)
    {
        var id = await reportHandler.HandleAsync(new ReportSnagCommand(
            locationId, subLocation, category, severity, description, User.GetStaffId(), User.GetStaffName()), ct);
        return new ReportSnagResult(id);
    }

    [McpServerTool(Name = "edit_snag")]
    [Description("Edits a Snag the caller reported, while it is still in the Reported state.")]
    public async Task EditSnag(
        [Description("The Snag's id.")] Guid snagId,
        [Description("New sub-location.")] string subLocation,
        [Description("New category.")] SnagCategory category,
        [Description("New severity.")] SnagSeverity severity,
        [Description("New description.")] string description,
        [Description("The Snag's current version, from a prior report_snag/get_snag/list_snags result — prevents overwriting a concurrent change.")] int expectedVersion,
        CancellationToken ct)
    {
        await editHandler.HandleAsync(new EditSnagCommand(
            snagId, User.GetStaffId(), subLocation, category, severity, description, expectedVersion), ct);
    }

    [McpServerTool(Name = "withdraw_snag")]
    [Description("Withdraws a Snag the caller reported, while it is still in the Reported state.")]
    public async Task WithdrawSnag(
        [Description("The Snag's id.")] Guid snagId,
        [Description("The Snag's current version.")] int expectedVersion,
        CancellationToken ct)
    {
        await withdrawHandler.HandleAsync(new WithdrawSnagCommand(snagId, User.GetStaffId(), expectedVersion), ct);
    }
}
