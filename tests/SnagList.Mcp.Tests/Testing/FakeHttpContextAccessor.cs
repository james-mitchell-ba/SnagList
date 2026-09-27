namespace SnagList.Mcp.Tests.Testing;

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using SnagList.Authorization;

public sealed class FakeHttpContextAccessor : IHttpContextAccessor
{
    public HttpContext? HttpContext { get; set; }

    public static FakeHttpContextAccessor For(string staffId, params string[] roles)
    {
        var claims = new List<Claim>
        {
            new(SnagListClaimTypes.StaffId, staffId),
            new(ClaimTypes.Name, "Test User"),
        };
        claims.AddRange(roles.Select(r => new Claim(SnagListClaimTypes.Role, r)));
        return new FakeHttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) },
        };
    }
}
