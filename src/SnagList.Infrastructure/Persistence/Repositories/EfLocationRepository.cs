namespace SnagList.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using SnagList.Application.Abstractions;
using SnagList.Domain.Locations;

public sealed class EfLocationRepository(SnagListDbContext dbContext) : ILocationRepository
{
    public Task<Location?> GetAsync(Guid id, CancellationToken ct) =>
        dbContext.Locations.FirstOrDefaultAsync(l => l.Id == id, ct);

    public void Add(Location location) => dbContext.Locations.Add(location);
}
