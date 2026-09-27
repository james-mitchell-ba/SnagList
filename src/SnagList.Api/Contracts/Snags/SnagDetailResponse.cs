namespace SnagList.Api.Contracts.Snags;

using SnagList.Api.Contracts;
using SnagList.Domain.Snags;

public sealed record SnagCommentResponse(Guid Id, string AuthorStaffId, string AuthorName, string Body, DateTimeOffset CreatedAt);

public sealed record SnagPhotoResponse(
    string PhotoKey, string FileName, string ContentType, long SizeBytes, DateTimeOffset UploadedAt, ApiLink Href);

public sealed class SnagDetailResponse : HypermediaResource
{
    public required Guid Id { get; init; }
    public required Guid LocationId { get; init; }
    public required string SubLocation { get; init; }
    public required SnagCategory Category { get; init; }
    public required SnagSeverity Severity { get; init; }
    public required string Description { get; init; }
    public required SnagStatus Status { get; init; }
    public required string ReportedByStaffId { get; init; }
    public required string ReportedByName { get; init; }
    public required DateTimeOffset ReportedAt { get; init; }
    public required int Version { get; init; }
    public required IReadOnlyList<SnagCommentResponse> Comments { get; init; }
    public required IReadOnlyList<SnagPhotoResponse> Photos { get; init; }
}
