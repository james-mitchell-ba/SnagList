namespace SnagList.Application.Locations.Queries;

using SnagList.Application.Abstractions;
using SnagList.Application.Common;

public sealed record ListLocationsQuery(bool IncludeRetired, string? Cursor, int Limit);

public sealed record LocationSummary(Guid Id, string Name, string Address, bool IsActive);

public sealed record LocationListCursorKey(string Name, Guid Id);

public sealed class ListLocationsQueryHandler(ILocationQueries queries)
{
    public Task<CursorPage<LocationSummary>> HandleAsync(
        ListLocationsQuery query, CancellationToken ct) => queries.ListAsync(query, ct);
}
