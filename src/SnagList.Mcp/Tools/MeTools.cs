namespace SnagList.Mcp.Tools;

using System.ComponentModel;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;
using SnagList.Authorization;
using SnagList.Domain.Staff;

public sealed record MeResult(string StaffId, string Name, string Email, IReadOnlyList<StaffRole> Roles);

[McpServerToolType]
public sealed class MeTools(IHttpContextAccessor httpContextAccessor)
{
    [McpServerTool(Name = "get_me")]
    [Description("Returns the calling staff member's identity and roles.")]
    public MeResult GetMe()
    {
        var user = httpContextAccessor.HttpContext!.User;
        return new MeResult(
            user.GetStaffId(),
            user.GetStaffName(),
            user.GetStaffEmail(),
            user.GetStaffRoles());
    }
}
