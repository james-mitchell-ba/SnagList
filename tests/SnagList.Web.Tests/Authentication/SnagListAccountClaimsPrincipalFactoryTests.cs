namespace SnagList.Web.Tests.Authentication;

using System.Text.Json;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication.Internal;
using SnagList.Authorization;
using SnagList.Web.Authentication;
using Xunit;

public class SnagListAccountClaimsPrincipalFactoryTests
{
    [Fact]
    public async Task CreateUserAsync_promotes_roles_array_and_staff_id_into_claims()
    {
        var account = JsonSerializer.Deserialize<RemoteUserAccount>("""
            {
                "sub": "oidc-subject",
                "name": "Jane Smith",
                "roles": ["Staff", "Maintenance"],
                "staff_id": "S-42"
            }
            """)!;
        var factory = new SnagListAccountClaimsPrincipalFactory(new FakeAccessTokenProviderAccessor());

        var principal = await factory.CreateUserAsync(account, new RemoteAuthenticationUserOptions());

        Assert.Equal(
            new[] { "Staff", "Maintenance" },
            principal.FindAll(SnagListClaimTypes.Role).Select(c => c.Value).ToArray());
        Assert.Equal("S-42", principal.FindFirst(SnagListClaimTypes.StaffId)?.Value);
    }

    [Fact]
    public async Task CreateUserAsync_ignores_missing_roles_and_staff_id()
    {
        var account = JsonSerializer.Deserialize<RemoteUserAccount>("""
            {
                "sub": "oidc-subject",
                "name": "Jane Smith"
            }
            """)!;
        var factory = new SnagListAccountClaimsPrincipalFactory(new FakeAccessTokenProviderAccessor());

        var principal = await factory.CreateUserAsync(account, new RemoteAuthenticationUserOptions());

        Assert.Empty(principal.FindAll(SnagListClaimTypes.Role));
        Assert.Null(principal.FindFirst(SnagListClaimTypes.StaffId));
    }

    private sealed class FakeAccessTokenProviderAccessor : IAccessTokenProviderAccessor
    {
        public IAccessTokenProvider TokenProvider => throw new NotSupportedException();
    }
}
