namespace SnagList.Infrastructure.Tests.Persistence;

using SnagList.Domain.Locations;
using SnagList.Domain.Snags;
using SnagList.Infrastructure.Persistence.Repositories;
using SnagList.Infrastructure.Tests.Testing;
using Xunit;

[Collection("Postgres")]
public class EfSnagRepositoryTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Add_then_GetAsync_round_trips_a_Snag_with_photos_and_comments()
    {
        await using var writeContext = fixture.CreateContext();
        var location = Location.Create("Head Office", "1 Main St");
        writeContext.Locations.Add(location);
        var snag = Snag.Report(
            location.Id, "3rd floor", SnagCategory.Electrical, SnagSeverity.Medium,
            "Flickering light", "U123456", "Jane Smith", DateTimeOffset.UtcNow);
        snag.AddPhoto(new SnagPhoto("blob-1", "photo1.jpg", "image/jpeg", 2048, DateTimeOffset.UtcNow));
        snag.AddComment("U999999", "Bob Maintenance", "Looking into it", DateTimeOffset.UtcNow);
        new EfSnagRepository(writeContext).Add(snag);
        await writeContext.SaveChangesAsync();

        await using var readContext = fixture.CreateContext();
        var loaded = await new EfSnagRepository(readContext).GetAsync(snag.Id, default);

        Assert.NotNull(loaded);
        Assert.Single(loaded!.Photos);
        Assert.Equal("photo1.jpg", loaded.Photos[0].FileName);
        Assert.Single(loaded.Comments);
        Assert.Equal("Looking into it", loaded.Comments[0].Body);
    }
}
