namespace SnagList.Api.Endpoints;

using Microsoft.AspNetCore.Mvc;
using SnagList.Api.Authorization;
using SnagList.Api.Contracts;
using SnagList.Application.Abstractions;
using SnagList.Api.Contracts.Snags;
using SnagList.Api.Hypermedia;
using SnagList.Application.Snags.Commands;
using SnagList.Application.Snags.Queries;
using SnagList.Domain.Snags;

public static class SnagEndpoints
{
    public static IEndpointRouteBuilder MapSnagEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/snags").RequireAuthorization(PolicyNames.Staff);

        group.MapPost("", async (
            ReportSnagRequest request, ReportSnagCommandHandler handler, HttpContext http, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(new ReportSnagCommand(
                request.LocationId, request.SubLocation, request.Category, request.Severity,
                request.Description, http.User.GetStaffId(), http.User.GetStaffName()), ct);
            return Results.Created($"/api/v1/snags/{id}", new { id });
        }).WithName("ReportSnag");

        group.MapGet("", async (
            ListSnagsQueryHandler handler, CancellationToken ct,
            [FromQuery] Guid? locationId = null, [FromQuery] SnagCategory? category = null,
            [FromQuery] SnagSeverity? severity = null, [FromQuery] SnagStatus? status = null,
            [FromQuery] string? cursor = null, [FromQuery] int limit = 20) =>
        {
            var page = await handler.HandleAsync(
                new ListSnagsQuery(locationId, category, severity, status, cursor, limit is > 0 and <= 100 ? limit : 20), ct);
            var items = page.Items.Select(s => new SnagSummaryResponse
            {
                Id = s.Id,
                LocationId = s.LocationId,
                SubLocation = s.SubLocation,
                Category = s.Category,
                Severity = s.Severity,
                Status = s.Status,
                ReportedByName = s.ReportedByName,
                ReportedAt = s.ReportedAt,
                Version = s.Version,
                Links = SnagLinksBuilder.BuildSummaryLinks(s.Id),
            }).ToList();
            return Results.Ok(new PagedResponse<SnagSummaryResponse>(items, page.NextCursor));
        }).WithName("ListSnags");

        group.MapGet("/{id:guid}", async (Guid id, GetSnagQueryHandler handler, HttpContext http, CancellationToken ct) =>
        {
            var detail = await handler.HandleAsync(new GetSnagQuery(id), ct);
            return detail is null ? Results.NotFound() : Results.Ok(ToDetailResponse(detail, http));
        }).WithName("GetSnag");

        group.MapPatch("/{id:guid}", async (
            Guid id, EditSnagRequest request, EditSnagCommandHandler handler, HttpContext http, CancellationToken ct) =>
        {
            await handler.HandleAsync(new EditSnagCommand(
                id, http.User.GetStaffId(), request.SubLocation, request.Category, request.Severity,
                request.Description, request.ExpectedVersion), ct);
            return Results.NoContent();
        }).WithName("EditSnag");

        group.MapPost("/{id:guid}/withdraw", async (
            Guid id, WithdrawSnagRequest request, WithdrawSnagCommandHandler handler, HttpContext http, CancellationToken ct) =>
        {
            await handler.HandleAsync(new WithdrawSnagCommand(id, http.User.GetStaffId(), request.ExpectedVersion), ct);
            return Results.NoContent();
        }).WithName("WithdrawSnag");

        Task<IResult> ChangeStatus(Guid id, ChangeSnagStatusRequest request, SnagStatus target,
            ChangeSnagStatusCommandHandler handler, HttpContext http, CancellationToken ct) =>
            handler.HandleAsync(new ChangeSnagStatusCommand(id, target, http.User.GetStaffId(), request.ExpectedVersion), ct)
                .ContinueWith<IResult>(_ => Results.NoContent(), ct);

        group.MapPost("/{id:guid}/acknowledge", (Guid id, ChangeSnagStatusRequest request, ChangeSnagStatusCommandHandler handler, HttpContext http, CancellationToken ct) =>
                ChangeStatus(id, request, SnagStatus.Acknowledged, handler, http, ct))
            .RequireAuthorization(PolicyNames.Maintenance).WithName("AcknowledgeSnag");

        group.MapPost("/{id:guid}/start", (Guid id, ChangeSnagStatusRequest request, ChangeSnagStatusCommandHandler handler, HttpContext http, CancellationToken ct) =>
                ChangeStatus(id, request, SnagStatus.InProgress, handler, http, ct))
            .RequireAuthorization(PolicyNames.Maintenance).WithName("StartSnagWork");

        group.MapPost("/{id:guid}/resolve", (Guid id, ChangeSnagStatusRequest request, ChangeSnagStatusCommandHandler handler, HttpContext http, CancellationToken ct) =>
                ChangeStatus(id, request, SnagStatus.Resolved, handler, http, ct))
            .RequireAuthorization(PolicyNames.Maintenance).WithName("ResolveSnag");

        group.MapPost("/{id:guid}/close", (Guid id, ChangeSnagStatusRequest request, ChangeSnagStatusCommandHandler handler, HttpContext http, CancellationToken ct) =>
                ChangeStatus(id, request, SnagStatus.Closed, handler, http, ct))
            .RequireAuthorization(PolicyNames.Maintenance).WithName("CloseSnag");

        group.MapPost("/{id:guid}/reject", async (Guid id, RejectSnagRequest request, RejectSnagCommandHandler handler, HttpContext http, CancellationToken ct) =>
            {
                await handler.HandleAsync(new RejectSnagCommand(
                    id, http.User.GetStaffId(), http.User.GetStaffName(), request.Reason, request.ExpectedVersion), ct);
                return Results.NoContent();
            })
            .RequireAuthorization(PolicyNames.Maintenance).WithName("RejectSnag");

        group.MapPost("/{id:guid}/comments", async (Guid id, AddSnagCommentRequest request, AddSnagCommentCommandHandler handler, HttpContext http, CancellationToken ct) =>
        {
            await handler.HandleAsync(new AddSnagCommentCommand(id, http.User.GetStaffId(), http.User.GetStaffName(), request.Body), ct);
            return Results.NoContent();
        }).WithName("AddSnagComment");

        group.MapPost("/{id:guid}/photos", async (Guid id, HttpRequest httpRequest, UploadSnagPhotoCommandHandler handler, CancellationToken ct) =>
        {
            if (!httpRequest.HasFormContentType) return Results.BadRequest();
            var form = await httpRequest.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file is null) return Results.BadRequest();

            await using var stream = file.OpenReadStream();
            await handler.HandleAsync(new UploadSnagPhotoCommand(id, file.FileName, file.ContentType, stream, file.Length), ct);
            return Results.NoContent();
        }).WithName("UploadSnagPhoto").DisableAntiforgery();

        group.MapGet("/{id:guid}/photos/{photoKey}", async (
            Guid id, string photoKey, GetSnagQueryHandler getSnag, IBlobStorage blobStorage, CancellationToken ct) =>
        {
            var detail = await getSnag.HandleAsync(new GetSnagQuery(id), ct);
            if (detail is null) return Results.NotFound();

            var blobKey = $"snags/{id}/{photoKey}";
            if (!detail.Photos.Any(p => p.BlobKey == blobKey)) return Results.NotFound();

            var url = await blobStorage.GetPresignedGetUrlAsync(blobKey, TimeSpan.FromMinutes(10), ct);
            return Results.Redirect(url.ToString());
        }).WithName("GetSnagPhoto");

        return app;
    }

    internal static SnagDetailResponse ToDetailResponse(SnagDetail detail, HttpContext http) => new()
    {
        Id = detail.Id,
        LocationId = detail.LocationId,
        SubLocation = detail.SubLocation,
        Category = detail.Category,
        Severity = detail.Severity,
        Description = detail.Description,
        Status = detail.Status,
        ReportedByStaffId = detail.ReportedByStaffId,
        ReportedByName = detail.ReportedByName,
        ReportedAt = detail.ReportedAt,
        Version = detail.Version,
        Comments = detail.Comments
            .Select(c => new SnagCommentResponse(c.Id, c.AuthorStaffId, c.AuthorName, c.Body, c.CreatedAt))
            .ToList(),
        Photos = detail.Photos.Select(p =>
        {
            var photoKey = PhotoKeyFrom(detail.Id, p.BlobKey);
            return new SnagPhotoResponse(photoKey, p.FileName, p.ContentType, p.SizeBytes, p.UploadedAt,
                new ApiLink($"/api/v1/snags/{detail.Id}/photos/{photoKey}", "GET", "GetSnagPhoto"));
        }).ToList(),
        Links = SnagLinksBuilder.Build(detail.Id, detail.Status, detail.ReportedByStaffId, http.User.GetStaffId(), http.User.GetStaffRoles()),
    };

    internal static string PhotoKeyFrom(Guid snagId, string blobKey) => blobKey[$"snags/{snagId}/".Length..];
}
