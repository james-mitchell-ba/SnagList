namespace SnagList.Application.Tests.Testing;

using SnagList.Application.Abstractions;
using SnagList.Domain.Snags;

public sealed class FakeSnagRepository : ISnagRepository
{
    public readonly Dictionary<Guid, Snag> Store = [];

    public Task<Snag?> GetAsync(Guid id, CancellationToken ct) => Task.FromResult(Store.GetValueOrDefault(id));

    public void Add(Snag snag) => Store[snag.Id] = snag;
}
