namespace SnagList.Web.Authentication;

using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication.Internal;
using SnagList.Authorization;

public class SnagListAccountClaimsPrincipalFactory(IAccessTokenProviderAccessor accessor)
    : AccountClaimsPrincipalFactory<RemoteUserAccount>(accessor)
{
    public override async ValueTask<ClaimsPrincipal> CreateUserAsync(
        RemoteUserAccount account,
        RemoteAuthenticationUserOptions options)
    {
        var user = await base.CreateUserAsync(account, options);

        if (account is not null && user.Identity is ClaimsIdentity identity)
        {
            PromoteClaims(identity, account, SnagListClaimTypes.Role);
            PromoteClaims(identity, account, SnagListClaimTypes.StaffId);
        }

        return user;
    }

    private static void PromoteClaims(ClaimsIdentity identity, RemoteUserAccount account, string claimType)
    {
        foreach (var existing in identity.FindAll(claimType).ToArray())
        {
            identity.RemoveClaim(existing);
        }

        if (!account.AdditionalProperties.TryGetValue(claimType, out var value) || value is not JsonElement element)
        {
            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                AddClaimIfPresent(identity, claimType, item.GetString());
            }
        }
        else
        {
            AddClaimIfPresent(identity, claimType, element.GetString());
        }
    }

    private static void AddClaimIfPresent(ClaimsIdentity identity, string claimType, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            identity.AddClaim(new Claim(claimType, value));
        }
    }
}
