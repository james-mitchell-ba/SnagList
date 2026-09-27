namespace SnagList.Infrastructure.Tests.Persistence;

using Microsoft.EntityFrameworkCore;
using SnagList.Application.Locations.Queries;
using SnagList.Domain.Locations;
using SnagList.Infrastructure.Persistence.Queries;
using SnagList.Infrastructure.Tests.Testing;
using Xunit;

[Collection("Postgres")]
public class EfLocationQueriesTests(PostgresFixture fixture)
{
    [Fact]
    public async Task ListAsync_pages_by_name_and_excludes_retired_by_default()
    {
        await using var context = fixture.CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE snag_comments, snag_photos, snags, locations, staff_identities, audit_log_entries",
            default(CancellationToken));
        var active1 = Location.Create("Engineering Site", "3 Park Rd");
        var active2 = Location.Create("Head Office", "1 Main St");
        var retired = Location.Create("Old Depot", "9 Yard Ln");
        retired.Retire();
        context.Locations.AddRange(active1, active2, retired);
        await context.SaveChangesAsync();

        var queries = new EfLocationQueries(context);
        var firstPage = await queries.ListAsync(new ListLocationsQuery(IncludeRetired: false, Cursor: null, Limit: 1), default);

        Assert.Single(firstPage.Items);
        Assert.Equal("Engineering Site", firstPage.Items[0].Name); // alphabetically first
        Assert.NotNull(firstPage.NextCursor);

        var secondPage = await queries.ListAsync(
            new ListLocationsQuery(IncludeRetired: false, Cursor: firstPage.NextCursor, Limit: 1), default);
        Assert.Single(secondPage.Items);
        Assert.Equal("Head Office", secondPage.Items[0].Name);
        Assert.Null(secondPage.NextCursor);
    }
}
