namespace SnagList.Application.Abstractions;

using SnagList.Application.Common;
using SnagList.Application.Locations.Queries;

public interface ILocationQueries
{
    Task<CursorPage<LocationSummary>> ListAsync(ListLocationsQuery query, CancellationToken ct);
}
