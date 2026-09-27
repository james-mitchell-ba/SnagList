namespace SnagList.Api.Auth.EntraId.Tests;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SnagList.Api.Auth.EntraId;
using Xunit;

public class EntraIdAuthenticationExtensionsTests
{
    private static IConfiguration Config(IDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void Throws_when_TenantId_is_missing()
    {
        var services = new ServiceCollection();
        var config = Config(new Dictionary<string, string?> { ["Auth:EntraId:Audience"] = "snaglist-api" });

        Assert.Throws<InvalidOperationException>(() => services.AddEntraIdAuthentication(config));
    }

    [Fact]
    public void Throws_when_Audience_is_missing()
    {
        var services = new ServiceCollection();
        var config = Config(new Dictionary<string, string?> { ["Auth:EntraId:TenantId"] = "11111111-1111-1111-1111-111111111111" });

        Assert.Throws<InvalidOperationException>(() => services.AddEntraIdAuthentication(config));
    }

    [Fact]
    public void Succeeds_and_registers_JwtBearer_when_both_are_present()
    {
        var services = new ServiceCollection();
        var config = Config(new Dictionary<string, string?>
        {
            ["Auth:EntraId:TenantId"] = "11111111-1111-1111-1111-111111111111",
            ["Auth:EntraId:Audience"] = "snaglist-api",
        });

        services.AddEntraIdAuthentication(config);

        Assert.Contains(services, d => d.ServiceType == typeof(Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider));
    }
}
