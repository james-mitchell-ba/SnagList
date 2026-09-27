namespace SnagList.Application.Locations;

public sealed class LocationNotFoundException(Guid id)
    : Exception($"Location {id} was not found.");
