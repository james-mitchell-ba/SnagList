namespace SnagList.Application.Tests.Snags;

using SnagList.Application.Locations;
using SnagList.Application.Snags;
using SnagList.Application.Snags.Commands;
using SnagList.Application.Tests.Testing;
using SnagList.Domain.Locations;
using SnagList.Domain.Snags;
using Xunit;

public class ReportSnagCommandTests
{
    private static (FakeLocationRepository locations, FakeSnagRepository snags, FakeUnitOfWork uow, ReportSnagCommandHandler handler)
        Build()
    {
        var locations = new FakeLocationRepository();
        var snags = new FakeSnagRepository();
        var uow = new FakeUnitOfWork();
        var handler = new ReportSnagCommandHandler(locations, snags, uow);
        return (locations, snags, uow, handler);
    }

    [Fact]
    public async Task Reports_a_Snag_against_an_active_Location()
    {
        var (locations, snags, uow, handler) = Build();
        var location = Location.Create("Head Office", "1 Main St");
        locations.Add(location);

        var id = await handler.HandleAsync(
            new ReportSnagCommand(
                location.Id, "3rd floor", SnagCategory.Electrical, SnagSeverity.Medium,
                "Flickering light", "U123456", "Jane Smith"),
            default);

        var stored = snags.Store[id];
        Assert.Equal(SnagStatus.Reported, stored.Status);
        Assert.Equal(location.Id, stored.LocationId);
        Assert.Equal(1, uow.SaveChangesCallCount);
    }

    [Fact]
    public async Task Throws_when_the_Location_does_not_exist()
    {
        var (_, _, _, handler) = Build();

        await Assert.ThrowsAsync<LocationNotFoundException>(() => handler.HandleAsync(
            new ReportSnagCommand(
                Guid.NewGuid(), "3rd floor", SnagCategory.Electrical, SnagSeverity.Medium,
                "Flickering light", "U123456", "Jane Smith"),
            default));
    }

    [Fact]
    public async Task Throws_when_the_Location_is_retired()
    {
        var (locations, _, _, handler) = Build();
        var location = Location.Create("Old Site", "1 Main St");
        location.Retire();
        locations.Add(location);

        await Assert.ThrowsAsync<LocationNotActiveException>(() => handler.HandleAsync(
            new ReportSnagCommand(
                location.Id, "3rd floor", SnagCategory.Electrical, SnagSeverity.Medium,
                "Flickering light", "U123456", "Jane Smith"),
            default));
    }
}
