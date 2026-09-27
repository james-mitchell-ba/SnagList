namespace SnagList.Application.Snags;

public sealed class SnagVersionConflictException(Guid snagId, int expectedVersion, int actualVersion)
    : Exception($"Snag {snagId} version conflict: expected {expectedVersion}, actual {actualVersion}.")
{
    public Guid SnagId { get; } = snagId;
    public int ExpectedVersion { get; } = expectedVersion;
    public int ActualVersion { get; } = actualVersion;
}
