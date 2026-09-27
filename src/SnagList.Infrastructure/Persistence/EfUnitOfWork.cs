namespace SnagList.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using SnagList.Application.Abstractions;
using SnagList.Application.Snags;
using SnagList.Domain.Snags;

public sealed class EfUnitOfWork(SnagListDbContext dbContext) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            var entry = ex.Entries.Single();
            if (entry.Entity is not Snag snag) throw;

            var expectedVersion = (int)entry.OriginalValues["Version"]!;
            var databaseValues = await entry.GetDatabaseValuesAsync(ct);
            var actualVersion = databaseValues is null ? expectedVersion : (int)databaseValues["Version"]!;
            throw new SnagVersionConflictException(snag.Id, expectedVersion, actualVersion);
        }
    }
}
