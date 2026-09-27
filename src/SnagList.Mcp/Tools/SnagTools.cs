namespace SnagList.Mcp.Tools;

using System.ComponentModel;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;
using SnagList.Application.Abstractions;
using SnagList.Application.Snags.Commands;
using SnagList.Application.Snags.Queries;
using SnagList.Authorization;
using SnagList.Domain.Snags;

public sealed record ReportSnagResult(Guid Id);
public sealed record GetSnagPhotoResult(string Url);

[McpServerToolType]
public sealed class SnagTools(
    IHttpContextAccessor httpContextAccessor,
    ReportSnagCommandHandler reportHandler,
    EditSnagCommandHandler editHandler,
    WithdrawSnagCommandHandler withdrawHandler,
    ChangeSnagStatusCommandHandler changeStatusHandler,
    RejectSnagCommandHandler rejectHandler,
    AddSnagCommentCommandHandler addCommentHandler,
    UploadSnagPhotoCommandHandler uploadPhotoHandler,
    GetSnagQueryHandler getSnagHandler,
    IBlobStorage blobStorage)
{
    private ClaimsPrincipal User => httpContextAccessor.HttpContext!.User;

    private void RequireMaintenance()
    {
        if (!AuthorizationPolicies.IsMaintenance(User.GetStaffRoles()))
        {
            throw new UnauthorizedAccessException("This operation requires the Maintenance role.");
        }
    }

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

    [McpServerTool(Name = "acknowledge_snag")]
    [Description("Maintenance confirms receipt of a Reported Snag.")]
    public Task AcknowledgeSnag(Guid snagId, int expectedVersion, CancellationToken ct) =>
        ChangeStatus(snagId, SnagStatus.Acknowledged, expectedVersion, ct);

    [McpServerTool(Name = "start_snag_work")]
    [Description("Maintenance marks an Acknowledged Snag as InProgress.")]
    public Task StartSnagWork(Guid snagId, int expectedVersion, CancellationToken ct) =>
        ChangeStatus(snagId, SnagStatus.InProgress, expectedVersion, ct);

    [McpServerTool(Name = "resolve_snag")]
    [Description("Maintenance marks an InProgress Snag as Resolved.")]
    public Task ResolveSnag(Guid snagId, int expectedVersion, CancellationToken ct) =>
        ChangeStatus(snagId, SnagStatus.Resolved, expectedVersion, ct);

    [McpServerTool(Name = "close_snag")]
    [Description("Closes a Resolved Snag, ending its lifecycle.")]
    public Task CloseSnag(Guid snagId, int expectedVersion, CancellationToken ct) =>
        ChangeStatus(snagId, SnagStatus.Closed, expectedVersion, ct);

    private Task ChangeStatus(Guid snagId, SnagStatus target, int expectedVersion, CancellationToken ct)
    {
        RequireMaintenance();
        return changeStatusHandler.HandleAsync(new ChangeSnagStatusCommand(snagId, target, User.GetStaffId(), expectedVersion), ct);
    }

    [McpServerTool(Name = "reject_snag")]
    [Description("Maintenance marks a Reported or Acknowledged Snag as invalid or a duplicate, with a reason.")]
    public Task RejectSnag(Guid snagId, string reason, int expectedVersion, CancellationToken ct)
    {
        RequireMaintenance();
        return rejectHandler.HandleAsync(new RejectSnagCommand(snagId, User.GetStaffId(), User.GetStaffName(), reason, expectedVersion), ct);
    }

    [McpServerTool(Name = "add_snag_comment")]
    [Description("Adds a follow-up remark to a Snag, regardless of its current status.")]
    public Task AddSnagComment(Guid snagId, string body, CancellationToken ct) =>
        addCommentHandler.HandleAsync(new AddSnagCommentCommand(snagId, User.GetStaffId(), User.GetStaffName(), body), ct);

    [McpServerTool(Name = "upload_snag_photo")]
    [Description("Attaches a photo to a Snag. A Snag may carry at most 5 photos.")]
    public async Task UploadSnagPhoto(
        Guid snagId,
        [Description("Original file name, e.g. 'light.jpg'.")] string fileName,
        [Description("MIME content type, e.g. 'image/jpeg'.")] string contentType,
        [Description("The photo's bytes, base64-encoded.")] string base64Content,
        CancellationToken ct)
    {
        var bytes = Convert.FromBase64String(base64Content);
        using var stream = new MemoryStream(bytes);
        await uploadPhotoHandler.HandleAsync(new UploadSnagPhotoCommand(snagId, fileName, contentType, stream, bytes.Length), ct);
    }

    [McpServerTool(Name = "get_snag_photo")]
    [Description("Returns a short-lived direct download URL for one of a Snag's photos.")]
    public async Task<GetSnagPhotoResult> GetSnagPhoto(
        Guid snagId,
        [Description("The photo's key, from a get_snag result's photos list.")] string photoKey,
        CancellationToken ct)
    {
        var detail = await getSnagHandler.HandleAsync(new GetSnagQuery(snagId), ct);
        var blobKey = $"snags/{snagId}/{photoKey}";
        if (detail is null || !detail.Photos.Any(p => p.BlobKey == blobKey))
        {
            throw new InvalidOperationException($"No photo '{photoKey}' found on Snag {snagId}.");
        }

        var url = await blobStorage.GetPresignedGetUrlAsync(blobKey, TimeSpan.FromMinutes(10), ct);
        return new GetSnagPhotoResult(url.ToString());
    }
}
