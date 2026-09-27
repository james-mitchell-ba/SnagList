namespace SnagList.Application.Abstractions;

using SnagList.Domain.Snags;

public interface ISnagRepository
{
    Task<Snag?> GetAsync(Guid id, CancellationToken ct);
    void Add(Snag snag);
}
