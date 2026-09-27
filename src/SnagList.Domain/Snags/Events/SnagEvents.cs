namespace SnagList.Domain.Snags.Events;

using SnagList.Domain.Snags;

public sealed record SnagReported(
    Guid SnagId, Guid LocationId, SnagSeverity Severity, string ReportedByStaffId, DateTimeOffset ReportedAt);

public sealed record SnagStatusChanged(
    Guid SnagId, SnagStatus PreviousStatus, SnagStatus NewStatus, string ChangedByStaffId,
    string ReportedByStaffId, DateTimeOffset ChangedAt);
