namespace SnagList.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using SnagList.Application.Abstractions;
using SnagList.Domain.Staff;

public sealed class EfStaffIdentityRepository(SnagListDbContext dbContext) : IStaffIdentityRepository
{
    public Task<StaffIdentity?> GetAsync(string staffId, CancellationToken ct) =>
        dbContext.StaffIdentities.FirstOrDefaultAsync(s => s.StaffId == staffId, ct);

    public void Add(StaffIdentity identity) => dbContext.StaffIdentities.Add(identity);
}
