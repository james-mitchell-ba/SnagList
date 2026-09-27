namespace SnagList.Infrastructure.Audit;

public sealed class AuditLogEntry
{
    public Guid Id { get; private set; }
    public string ActorStaffId { get; private set; } = "";
    public string Action { get; private set; } = "";
    public string EntityType { get; private set; } = "";
    public Guid EntityId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    private AuditLogEntry() { } // EF Core

    public AuditLogEntry(string actorStaffId, string action, string entityType, Guid entityId, DateTimeOffset occurredAt)
    {
        Id = Guid.NewGuid();
        ActorStaffId = actorStaffId;
        Action = action;
        EntityType = entityType;
        EntityId = entityId;
        OccurredAt = occurredAt;
    }
}
