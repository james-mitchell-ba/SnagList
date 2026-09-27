namespace SnagList.Infrastructure.Tests.Persistence;

using SnagList.Domain.Locations;
using SnagList.Infrastructure.Persistence.Repositories;
using SnagList.Infrastructure.Tests.Testing;
using Xunit;

[Collection("Postgres")]
public class EfLocationRepositoryTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Add_then_GetAsync_round_trips_a_Location()
    {
        await using var writeContext = fixture.CreateContext();
        var location = Location.Create("Head Office", "1 Main St");
        new EfLocationRepository(writeContext).Add(location);
        await writeContext.SaveChangesAsync();

        await using var readContext = fixture.CreateContext();
        var loaded = await new EfLocationRepository(readContext).GetAsync(location.Id, default);

        Assert.NotNull(loaded);
        Assert.Equal("Head Office", loaded!.Name);
        Assert.True(loaded.IsActive);
    }

    [Fact]
    public async Task GetAsync_returns_null_for_an_unknown_id()
    {
        await using var context = fixture.CreateContext();
        var loaded = await new EfLocationRepository(context).GetAsync(Guid.NewGuid(), default);
        Assert.Null(loaded);
    }
}
