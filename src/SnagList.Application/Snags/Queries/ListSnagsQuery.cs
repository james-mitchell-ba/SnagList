namespace SnagList.Application.Snags.Queries;

using SnagList.Application.Abstractions;
using SnagList.Application.Common;
using SnagList.Domain.Snags;

public sealed class ListSnagsQueryHandler(ISnagQueries queries)
{
    public Task<CursorPage<SnagSummary>> HandleAsync(ListSnagsQuery query, CancellationToken ct) =>
        queries.ListAsync(query, ct);
}

public sealed record ListSnagsQuery(
    Guid? LocationId, SnagCategory? Category, SnagSeverity? Severity, SnagStatus? Status,
    string? Cursor, int Limit);

public sealed record SnagSummary(
    Guid Id, Guid LocationId, string SubLocation, SnagCategory Category, SnagSeverity Severity,
    SnagStatus Status, string ReportedByName, DateTimeOffset ReportedAt, int Version);

public sealed record SnagListCursorKey(DateTimeOffset ReportedAt, Guid Id);
