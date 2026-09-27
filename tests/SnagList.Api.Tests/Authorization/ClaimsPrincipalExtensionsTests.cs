namespace SnagList.Api.Tests.Authorization;

using System.Security.Claims;
using SnagList.Api.Authorization;
using SnagList.Domain.Staff;
using Xunit;

public class ClaimsPrincipalExtensionsTests
{
    private static ClaimsPrincipal PrincipalWith(params Claim[] claims) => new(new ClaimsIdentity(claims, "test"));

    [Fact]
    public void GetStaffId_reads_the_staff_id_claim()
    {
        var principal = PrincipalWith(new Claim(SnagListClaimTypes.StaffId, "U123456"));
        Assert.Equal("U123456", principal.GetStaffId());
    }

    [Fact]
    public void GetStaffId_throws_when_the_claim_is_missing()
    {
        Assert.Throws<InvalidOperationException>(() => PrincipalWith().GetStaffId());
    }

    [Fact]
    public void GetStaffRoles_parses_recognised_values_and_ignores_the_rest()
    {
        var principal = PrincipalWith(
            new Claim(SnagListClaimTypes.Role, "Staff"),
            new Claim(SnagListClaimTypes.Role, "Maintenance"),
            new Claim(SnagListClaimTypes.Role, "SomeUnrelatedAppRole"));

        var roles = principal.GetStaffRoles();

        Assert.Contains(StaffRole.Staff, roles);
        Assert.Contains(StaffRole.Maintenance, roles);
        Assert.Equal(2, roles.Count);
    }
}
