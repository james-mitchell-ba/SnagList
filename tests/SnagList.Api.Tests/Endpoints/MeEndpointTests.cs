namespace SnagList.Api.Tests.Endpoints;

using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SnagList.Api.Endpoints;
using SnagList.Api.Tests.Testing;
using SnagList.Infrastructure.Persistence;
using Xunit;

public class MeEndpointTests(SnagListApiFactory factory) : IClassFixture<SnagListApiFactory>
{
    [Fact]
    public async Task Returns_the_callers_identity_and_roles()
    {
        var client = factory.CreateAuthenticatedClient("U100030", "Staff", "Maintenance");

        var me = await client.GetFromJsonAsync<MeResponse>("/api/v1/me");

        Assert.Equal("U100030", me!.StaffId);
        Assert.Contains(SnagList.Domain.Staff.StaffRole.Maintenance, me.Roles);
    }

    [Fact]
    public async Task Syncs_a_StaffIdentity_row_on_first_authenticated_request()
    {
        var client = factory.CreateAuthenticatedClient("U100031", "Staff");

        await client.GetAsync("/api/v1/me");

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SnagListDbContext>();
        var identity = await dbContext.StaffIdentities.FirstOrDefaultAsync(s => s.StaffId == "U100031");
        Assert.NotNull(identity);
    }
}
