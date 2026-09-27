namespace SnagList.Authorization;

using System.Security.Claims;
using SnagList.Domain.Staff;

public static class ClaimsPrincipalExtensions
{
    public static string GetStaffId(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(SnagListClaimTypes.StaffId)
            ?? throw new InvalidOperationException("Token has no staff_id claim.");

    public static string GetStaffName(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.Name) ?? principal.FindFirstValue("name") ?? "";

    public static string GetStaffEmail(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.Email) ?? principal.FindFirstValue("email") ?? "";

    public static IReadOnlyList<StaffRole> GetStaffRoles(this ClaimsPrincipal principal) =>
        principal.FindAll(SnagListClaimTypes.Role)
            .Select(c => Enum.TryParse<StaffRole>(c.Value, ignoreCase: true, out var role) ? role : (StaffRole?)null)
            .Where(r => r is not null)
            .Select(r => r!.Value)
            .Distinct()
            .ToList();
}
