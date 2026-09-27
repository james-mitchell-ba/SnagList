namespace SnagList.Contracts.Locations;

public sealed record CreateLocationRequest(string Name, string Address);
public sealed record UpdateLocationRequest(string Name, string Address);
