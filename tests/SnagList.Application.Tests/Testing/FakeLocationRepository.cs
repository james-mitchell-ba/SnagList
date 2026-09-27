namespace SnagList.Application.Tests.Testing;

using SnagList.Application.Abstractions;
using SnagList.Domain.Locations;

public sealed class FakeLocationRepository : ILocationRepository
{
    public readonly Dictionary<Guid, Location> Store = [];

    public Task<Location?> GetAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(Store.GetValueOrDefault(id));

    public void Add(Location location) => Store[location.Id] = location;
}
