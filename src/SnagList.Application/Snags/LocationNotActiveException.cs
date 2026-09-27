namespace SnagList.Application.Snags;

public sealed class LocationNotActiveException(Guid locationId)
    : Exception($"Location {locationId} is retired and cannot accept new Snag reports.");
