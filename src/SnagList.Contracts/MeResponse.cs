namespace SnagList.Contracts;

using SnagList.Domain.Staff;

public sealed record MeResponse(string StaffId, string Name, string Email, IReadOnlyList<StaffRole> Roles);
