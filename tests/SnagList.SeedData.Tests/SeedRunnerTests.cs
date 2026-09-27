namespace SnagList.SeedData.Tests;

using Microsoft.EntityFrameworkCore;
using Xunit;

public class SeedRunnerTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task RunAsync_migrates_and_seeds_demo_data()
    {
        await using var dbContext = fixture.CreateContext();

        await SeedRunner.RunAsync(dbContext, TextWriter.Null);

        Assert.Equal(3, await dbContext.Locations.CountAsync());
        Assert.Equal(2, await dbContext.Snags.CountAsync());
    }

    [Fact]
    public async Task RunAsync_is_idempotent_across_repeated_runs()
    {
        await using (var firstRun = fixture.CreateContext()) await SeedRunner.RunAsync(firstRun, TextWriter.Null);
        await using var secondRun = fixture.CreateContext();

        await SeedRunner.RunAsync(secondRun, TextWriter.Null);

        Assert.Equal(3, await secondRun.Locations.CountAsync());
    }

    [Fact]
    public async Task RunAsync_with_reseed_wipes_existing_data_before_reseeding()
    {
        await using (var firstRun = fixture.CreateContext()) await SeedRunner.RunAsync(firstRun, TextWriter.Null);
        await using var firstRunReadContext = fixture.CreateContext();
        var firstLocationId = (await firstRunReadContext.Locations.FirstAsync()).Id;

        await using var reseedContext = fixture.CreateContext();
        await SeedRunner.RunAsync(reseedContext, TextWriter.Null, reseed: true);

        await using var afterReseed = fixture.CreateContext();
        Assert.Equal(3, await afterReseed.Locations.CountAsync());
        // A genuinely fresh set of rows, not the same ones re-detected as "already present."
        Assert.DoesNotContain(await afterReseed.Locations.Select(l => l.Id).ToListAsync(), id => id == firstLocationId);
    }
}
