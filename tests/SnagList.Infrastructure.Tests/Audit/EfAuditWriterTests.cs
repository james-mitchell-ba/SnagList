namespace SnagList.Infrastructure.Tests.Audit;

using Microsoft.EntityFrameworkCore;
using SnagList.Infrastructure.Audit;
using SnagList.Infrastructure.Tests.Testing;
using Xunit;

[Collection("Postgres")]
public class EfAuditWriterTests(PostgresFixture fixture)
{
    [Fact]
    public async Task WriteAsync_persists_an_audit_log_entry()
    {
        await using var context = fixture.CreateContext();
        var writer = new EfAuditWriter(context);
        var entityId = Guid.NewGuid();

        await writer.WriteAsync("U123456", "SnagReported", "Snag", entityId, DateTimeOffset.UtcNow, default);

        await using var readContext = fixture.CreateContext();
        var entry = await readContext.AuditLogEntries.SingleAsync(a => a.EntityId == entityId);
        Assert.Equal("U123456", entry.ActorStaffId);
        Assert.Equal("SnagReported", entry.Action);
    }
}
