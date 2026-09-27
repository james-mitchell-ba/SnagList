namespace SnagList.Domain.Snags;

public sealed class InvalidSnagStatusTransitionException : Exception
{
    public SnagStatus From { get; }
    public SnagStatus To { get; }

    public InvalidSnagStatusTransitionException(SnagStatus from, SnagStatus to)
        : base($"Cannot transition a Snag from {from} to {to}.")
    {
        From = from;
        To = to;
    }
}
