namespace SnagList.Authorization;

using SnagList.Domain.Staff;

public static class AuthorizationPolicies
{
    public static bool IsStaff(IReadOnlyList<StaffRole> roles) => roles.Count > 0;
    public static bool IsMaintenance(IReadOnlyList<StaffRole> roles) => roles.Contains(StaffRole.Maintenance);
}
