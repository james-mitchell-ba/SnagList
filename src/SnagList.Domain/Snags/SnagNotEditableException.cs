namespace SnagList.Domain.Snags;

public sealed class SnagNotEditableException : Exception
{
    public SnagNotEditableException(SnagStatus status)
        : base($"A Snag can only be edited or withdrawn while Reported; current status is {status}.") { }
}
