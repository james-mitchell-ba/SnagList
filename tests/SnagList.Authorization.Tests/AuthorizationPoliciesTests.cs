namespace SnagList.Authorization.Tests;

using SnagList.Authorization;
using SnagList.Domain.Staff;
using Xunit;

public class AuthorizationPoliciesTests
{
    [Fact]
    public void IsStaff_is_true_for_either_role()
    {
        Assert.True(AuthorizationPolicies.IsStaff([StaffRole.Staff]));
        Assert.True(AuthorizationPolicies.IsStaff([StaffRole.Maintenance]));
    }

    [Fact]
    public void IsStaff_is_false_for_no_recognised_roles()
    {
        Assert.False(AuthorizationPolicies.IsStaff([]));
    }

    [Fact]
    public void IsMaintenance_requires_the_Maintenance_role_specifically()
    {
        Assert.False(AuthorizationPolicies.IsMaintenance([StaffRole.Staff]));
        Assert.True(AuthorizationPolicies.IsMaintenance([StaffRole.Maintenance]));
    }
}
