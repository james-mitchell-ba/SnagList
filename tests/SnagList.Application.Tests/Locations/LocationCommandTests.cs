namespace SnagList.Application.Tests.Locations;

using SnagList.Application.Locations;
using SnagList.Application.Locations.Commands;
using SnagList.Application.Tests.Testing;
using Xunit;

public class LocationCommandTests
{
    [Fact]
    public async Task CreateLocationCommandHandler_adds_an_active_location_and_saves()
    {
        var repository = new FakeLocationRepository();
        var unitOfWork = new FakeUnitOfWork();
        var handler = new CreateLocationCommandHandler(repository, unitOfWork);

        var id = await handler.HandleAsync(new CreateLocationCommand("Head Office", "1 Main St"), default);

        var stored = repository.Store[id];
        Assert.Equal("Head Office", stored.Name);
        Assert.True(stored.IsActive);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task UpdateLocationCommandHandler_updates_an_existing_location()
    {
        var repository = new FakeLocationRepository();
        var unitOfWork = new FakeUnitOfWork();
        var location = SnagList.Domain.Locations.Location.Create("Head Office", "1 Main St");
        repository.Add(location);
        var handler = new UpdateLocationCommandHandler(repository, unitOfWork);

        await handler.HandleAsync(new UpdateLocationCommand(location.Id, "Renamed", "2 Main St"), default);

        Assert.Equal("Renamed", repository.Store[location.Id].Name);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task UpdateLocationCommandHandler_throws_when_not_found()
    {
        var handler = new UpdateLocationCommandHandler(new FakeLocationRepository(), new FakeUnitOfWork());

        await Assert.ThrowsAsync<LocationNotFoundException>(
            () => handler.HandleAsync(new UpdateLocationCommand(Guid.NewGuid(), "X", "Y"), default));
    }

    [Fact]
    public async Task RetireLocationCommandHandler_sets_IsActive_false()
    {
        var repository = new FakeLocationRepository();
        var unitOfWork = new FakeUnitOfWork();
        var location = SnagList.Domain.Locations.Location.Create("Head Office", "1 Main St");
        repository.Add(location);
        var handler = new RetireLocationCommandHandler(repository, unitOfWork);

        await handler.HandleAsync(new RetireLocationCommand(location.Id), default);

        Assert.False(repository.Store[location.Id].IsActive);
    }
}
