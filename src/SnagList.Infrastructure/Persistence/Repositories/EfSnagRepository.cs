namespace SnagList.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using SnagList.Application.Abstractions;
using SnagList.Domain.Snags;

public sealed class EfSnagRepository(SnagListDbContext dbContext) : ISnagRepository
{
    public Task<Snag?> GetAsync(Guid id, CancellationToken ct) =>
        dbContext.Snags
            .Include(s => s.Photos)
            .Include(s => s.Comments)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public void Add(Snag snag) => dbContext.Snags.Add(snag);
}
