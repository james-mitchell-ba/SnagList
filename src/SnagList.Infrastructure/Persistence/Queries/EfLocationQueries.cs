namespace SnagList.Infrastructure.Persistence.Queries;

using Microsoft.EntityFrameworkCore;
using SnagList.Application.Abstractions;
using SnagList.Application.Common;
using SnagList.Application.Locations.Queries;

public sealed class EfLocationQueries(SnagListDbContext dbContext) : ILocationQueries
{
    public async Task<CursorPage<LocationSummary>> ListAsync(ListLocationsQuery query, CancellationToken ct)
    {
        var cursorKey = OpaqueCursor.Decode<LocationListCursorKey>(query.Cursor);

        var q = dbContext.Locations.AsNoTracking().AsQueryable();
        if (!query.IncludeRetired) q = q.Where(l => l.IsActive);
        if (cursorKey is not null)
        {
            q = q.Where(l => l.Name.CompareTo(cursorKey.Name) > 0
                || (l.Name == cursorKey.Name && l.Id.CompareTo(cursorKey.Id) > 0));
        }

        var rows = await q.OrderBy(l => l.Name).ThenBy(l => l.Id)
            .Take(query.Limit + 1)
            .Select(l => new LocationSummary(l.Id, l.Name, l.Address, l.IsActive))
            .ToListAsync(ct);

        var hasMore = rows.Count > query.Limit;
        var page = hasMore ? rows.Take(query.Limit).ToList() : rows;
        var nextCursor = hasMore
            ? OpaqueCursor.Encode(new LocationListCursorKey(page[^1].Name, page[^1].Id))
            : null;
        return new CursorPage<LocationSummary>(page, nextCursor);
    }
}
