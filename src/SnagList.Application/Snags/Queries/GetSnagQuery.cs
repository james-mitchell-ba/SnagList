namespace SnagList.Application.Snags.Queries;

using SnagList.Application.Abstractions;
using SnagList.Application.Common;
using SnagList.Domain.Snags;

public sealed class GetSnagQueryHandler(ISnagQueries queries)
{
    public Task<SnagDetail?> HandleAsync(GetSnagQuery query, CancellationToken ct) =>
        queries.GetAsync(query.SnagId, ct);
}

public sealed record GetSnagQuery(Guid SnagId);

public sealed record SnagCommentDto(Guid Id, string AuthorStaffId, string AuthorName, string Body, DateTimeOffset CreatedAt);

public sealed record SnagPhotoDto(string BlobKey, string FileName, string ContentType, long SizeBytes, DateTimeOffset UploadedAt);

public sealed record SnagDetail(
    Guid Id, Guid LocationId, string SubLocation, SnagCategory Category, SnagSeverity Severity,
    string Description, SnagStatus Status, string ReportedByStaffId, string ReportedByName,
    DateTimeOffset ReportedAt, int Version,
    IReadOnlyList<SnagCommentDto> Comments, IReadOnlyList<SnagPhotoDto> Photos);
