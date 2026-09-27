namespace SnagList.Application.Snags;

public sealed class SnagNotFoundException(Guid id) : Exception($"Snag {id} was not found.");
