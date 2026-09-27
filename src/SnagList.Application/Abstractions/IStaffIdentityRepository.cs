namespace SnagList.Application.Abstractions;

using SnagList.Domain.Staff;

public interface IStaffIdentityRepository
{
    Task<StaffIdentity?> GetAsync(string staffId, CancellationToken ct);
    void Add(StaffIdentity identity);
}
