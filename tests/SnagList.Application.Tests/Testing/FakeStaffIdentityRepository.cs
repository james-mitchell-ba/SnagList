namespace SnagList.Application.Tests.Testing;

using SnagList.Application.Abstractions;
using SnagList.Domain.Staff;

public sealed class FakeStaffIdentityRepository : IStaffIdentityRepository
{
    public readonly Dictionary<string, StaffIdentity> Store = [];

    public Task<StaffIdentity?> GetAsync(string staffId, CancellationToken ct) =>
        Task.FromResult(Store.GetValueOrDefault(staffId));

    public void Add(StaffIdentity identity) => Store[identity.StaffId] = identity;
}
