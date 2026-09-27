namespace SnagList.Application.Abstractions;

using SnagList.Application.Common;
using SnagList.Application.Snags.Queries;

public interface ISnagQueries
{
    Task<CursorPage<SnagSummary>> ListAsync(ListSnagsQuery query, CancellationToken ct);
    Task<SnagDetail?> GetAsync(Guid id, CancellationToken ct);
}
