namespace SnagList.Authorization;

using System.Security.Claims;
using SnagList.Domain.Staff;

public static class ClaimsPrincipalExtensions
{
    private static string? FindValue(this ClaimsPrincipal principal, string claimType) =>
        principal.FindFirst(claimType)?.Value;

    public static string GetStaffId(this ClaimsPrincipal principal) =>
        principal.FindValue(SnagListClaimTypes.StaffId)
            ?? throw new InvalidOperationException("Token has no staff_id claim.");

    public static string GetStaffName(this ClaimsPrincipal principal) =>
        principal.FindValue(ClaimTypes.Name) ?? principal.FindValue("name") ?? "";

    public static string GetStaffEmail(this ClaimsPrincipal principal) =>
        principal.FindValue(ClaimTypes.Email) ?? principal.FindValue("email") ?? "";

    public static IReadOnlyList<StaffRole> GetStaffRoles(this ClaimsPrincipal principal) =>
        principal.FindAll(SnagListClaimTypes.Role)
            .Select(c => Enum.TryParse<StaffRole>(c.Value, ignoreCase: true, out var role) ? role : (StaffRole?)null)
            .Where(r => r is not null)
            .Select(r => r!.Value)
            .Distinct()
            .ToList();
}
