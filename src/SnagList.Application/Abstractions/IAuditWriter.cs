namespace SnagList.Application.Abstractions;

public interface IAuditWriter
{
    Task WriteAsync(
        string actorStaffId, string action, string entityType, Guid entityId,
        DateTimeOffset occurredAt, CancellationToken ct);
}
