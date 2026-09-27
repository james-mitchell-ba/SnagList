namespace SnagList.Application.Abstractions;

using SnagList.Domain.Locations;

public interface ILocationRepository
{
    Task<Location?> GetAsync(Guid id, CancellationToken ct);
    void Add(Location location);
}
