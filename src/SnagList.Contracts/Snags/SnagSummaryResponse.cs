namespace SnagList.Contracts.Snags;

using SnagList.Contracts;
using SnagList.Domain.Snags;

public sealed class SnagSummaryResponse : HypermediaResource
{
    public required Guid Id { get; init; }
    public required Guid LocationId { get; init; }
    public required string SubLocation { get; init; }
    public required SnagCategory Category { get; init; }
    public required SnagSeverity Severity { get; init; }
    public required SnagStatus Status { get; init; }
    public required string ReportedByName { get; init; }
    public required DateTimeOffset ReportedAt { get; init; }
    public required int Version { get; init; }
}
