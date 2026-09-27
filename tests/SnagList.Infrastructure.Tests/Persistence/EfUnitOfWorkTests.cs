namespace SnagList.Infrastructure.Tests.Persistence;

using Microsoft.EntityFrameworkCore;
using SnagList.Application.Notifications;
using SnagList.Application.Snags;
using SnagList.Domain.Locations;
using SnagList.Domain.Snags;
using SnagList.Infrastructure.Persistence;
using SnagList.Infrastructure.Persistence.Repositories;
using SnagList.Infrastructure.Tests.Testing;
using Xunit;

[Collection("Postgres")]
public class EfUnitOfWorkTests(PostgresFixture fixture)
{
    [Fact]
    public async Task SaveChangesAsync_translates_a_real_concurrency_conflict_into_SnagVersionConflictException()
    {
        await using var setupContext = fixture.CreateContext();
        var location = Location.Create("Head Office", "1 Main St");
        setupContext.Locations.Add(location);
        var snag = Snag.Report(
            location.Id, "3rd floor", SnagCategory.Electrical, SnagSeverity.Low,
            "desc", "U1", "Jane", DateTimeOffset.UtcNow);
        setupContext.Snags.Add(snag);
        await setupContext.SaveChangesAsync();

        await using var contextA = fixture.CreateContext();
        await using var contextB = fixture.CreateContext();
        var snagA = await contextA.Snags.FirstAsync(s => s.Id == snag.Id);
        var snagB = await contextB.Snags.FirstAsync(s => s.Id == snag.Id);

        var noopDispatcher = new SnagNotificationDispatcher(
            new NoOpEmailSender(), new EfStaffIdentityRepository(contextA), new NotificationOptions());

        snagA.Edit("4th floor", SnagCategory.Electrical, SnagSeverity.Low, "edited by A");
        await new EfUnitOfWork(contextA, noopDispatcher).SaveChangesAsync(default);

        snagB.Edit("5th floor", SnagCategory.Electrical, SnagSeverity.Low, "edited by B, stale");
        var ex = await Assert.ThrowsAsync<SnagVersionConflictException>(
            () => new EfUnitOfWork(contextB, noopDispatcher).SaveChangesAsync(default));

        Assert.Equal(snag.Id, ex.SnagId);
    }
}
