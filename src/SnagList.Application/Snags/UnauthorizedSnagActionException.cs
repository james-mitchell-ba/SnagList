namespace SnagList.Application.Snags;

public sealed class UnauthorizedSnagActionException(string action)
    : Exception($"Not authorized to {action} this Snag.");
