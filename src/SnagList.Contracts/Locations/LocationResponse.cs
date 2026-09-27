namespace SnagList.Contracts.Locations;

public sealed class LocationResponse : HypermediaResource
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Address { get; init; }
    public required bool IsActive { get; init; }
}
