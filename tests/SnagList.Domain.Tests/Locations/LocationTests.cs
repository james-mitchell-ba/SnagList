namespace SnagList.Domain.Tests.Locations;

using SnagList.Domain.Locations;
using Xunit;

public class LocationTests
{
    [Fact]
    public void Create_starts_active()
    {
        var location = Location.Create("Head Office", "1 Main St");

        Assert.True(location.IsActive);
        Assert.Equal("Head Office", location.Name);
        Assert.Equal("1 Main St", location.Address);
        Assert.NotEqual(Guid.Empty, location.Id);
    }

    [Fact]
    public void Update_changes_name_and_address()
    {
        var location = Location.Create("Head Office", "1 Main St");

        location.Update("Head Office (renamed)", "2 Main St");

        Assert.Equal("Head Office (renamed)", location.Name);
        Assert.Equal("2 Main St", location.Address);
    }

    [Fact]
    public void Retire_sets_IsActive_false()
    {
        var location = Location.Create("Head Office", "1 Main St");

        location.Retire();

        Assert.False(location.IsActive);
    }
}
