namespace SnagList.Infrastructure.Tests.Persistence;

using SnagList.Application.Snags.Queries;
using SnagList.Domain.Locations;
using SnagList.Domain.Snags;
using SnagList.Infrastructure.Persistence.Queries;
using SnagList.Infrastructure.Tests.Testing;
using Xunit;

[Collection("Postgres")]
public class EfSnagQueriesTests(PostgresFixture fixture)
{
    [Fact]
    public async Task ListAsync_pages_newest_first_and_filters_by_status()
    {
        await using var context = fixture.CreateContext();
        var location = Location.Create("Head Office", "1 Main St");
        context.Locations.Add(location);
        var older = Snag.Report(location.Id, "3rd floor", SnagCategory.Electrical, SnagSeverity.Low,
            "older", "U1", "Jane", DateTimeOffset.UtcNow.AddHours(-2));
        var newer = Snag.Report(location.Id, "4th floor", SnagCategory.Plumbing, SnagSeverity.Low,
            "newer", "U1", "Jane", DateTimeOffset.UtcNow.AddHours(-1));
        newer.TransitionTo(SnagStatus.Acknowledged, "U9", DateTimeOffset.UtcNow);
        context.Snags.AddRange(older, newer);
        await context.SaveChangesAsync();

        var queries = new EfSnagQueries(context);
        var page = await queries.ListAsync(
            new ListSnagsQuery(LocationId: null, Category: null, Severity: null, Status: SnagStatus.Acknowledged, Cursor: null, Limit: 10),
            default);

        var summary = Assert.Single(page.Items);
        Assert.Equal(newer.Id, summary.Id);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task GetAsync_returns_comments_and_photos()
    {
        await using var context = fixture.CreateContext();
        var location = Location.Create("Head Office", "1 Main St");
        context.Locations.Add(location);
        var snag = Snag.Report(location.Id, "3rd floor", SnagCategory.Electrical, SnagSeverity.Low,
            "desc", "U1", "Jane", DateTimeOffset.UtcNow);
        snag.AddComment("U9", "Bob", "noted", DateTimeOffset.UtcNow);
        context.Snags.Add(snag);
        await context.SaveChangesAsync();

        var detail = await new EfSnagQueries(context).GetAsync(snag.Id, default);

        Assert.NotNull(detail);
        Assert.Single(detail!.Comments);
        Assert.Equal("noted", detail.Comments[0].Body);
    }

    [Fact]
    public async Task GetAsync_returns_null_for_an_unknown_id()
    {
        await using var context = fixture.CreateContext();
        var detail = await new EfSnagQueries(context).GetAsync(Guid.NewGuid(), default);
        Assert.Null(detail);
    }
}
