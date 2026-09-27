namespace SnagList.Mcp.Tests.Tools;

using SnagList.Mcp.Tests.Testing;
using SnagList.Mcp.Tools;
using SnagList.Domain.Staff;
using Xunit;

[Collection("McpTools")]
public class MeToolsTests(McpToolsFixture fixture)
{
    [Fact]
    public void GetMe_returns_the_callers_identity_and_roles()
    {
        using var scope = fixture.CreateScope();
        var tools = fixture.CreateTool<MeTools>(scope, "U100063", "Staff", "Maintenance");

        dynamic me = tools.GetMe();

        Assert.Equal("U100063", (string)me.StaffId);
        Assert.Contains(StaffRole.Maintenance, (IReadOnlyList<StaffRole>)me.Roles);
    }
}
