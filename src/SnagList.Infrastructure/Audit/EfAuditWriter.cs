namespace SnagList.Infrastructure.Audit;

using SnagList.Application.Abstractions;
using SnagList.Infrastructure.Persistence;

public sealed class EfAuditWriter(SnagListDbContext dbContext) : IAuditWriter
{
    public async Task WriteAsync(
        string actorStaffId, string action, string entityType, Guid entityId,
        DateTimeOffset occurredAt, CancellationToken ct)
    {
        dbContext.AuditLogEntries.Add(new AuditLogEntry(actorStaffId, action, entityType, entityId, occurredAt));
        await dbContext.SaveChangesAsync(ct);
    }
}
