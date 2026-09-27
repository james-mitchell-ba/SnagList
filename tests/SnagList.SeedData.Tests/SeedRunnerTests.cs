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
}
