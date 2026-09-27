namespace SnagList.SeedData;

using Microsoft.EntityFrameworkCore;
using SnagList.Domain.Locations;
using SnagList.Domain.Snags;
using SnagList.Infrastructure.Persistence;

public static class SeedRunner
{
    public static async Task RunAsync(SnagListDbContext dbContext, TextWriter output)
    {
        await dbContext.Database.MigrateAsync();

        if (await dbContext.Locations.AnyAsync())
        {
            output.WriteLine("Demo data already present; skipping seed.");
            return;
        }

        output.WriteLine("Seeding demo data...");
        var headOffice = Location.Create("Head Office", "1 Main St, London");
        var northernOffice = Location.Create("Northern Office", "42 North Rd, Leeds");
        var engineeringSite = Location.Create("Engineering Site", "3 Park Rd, Manchester");
        dbContext.Locations.AddRange(headOffice, northernOffice, engineeringSite);

        var snag1 = Snag.Report(
            headOffice.Id, "3rd floor, room 3.12", SnagCategory.Electrical, SnagSeverity.Medium,
            "Flickering light above the kitchenette", "U100001", "Jane Smith", DateTimeOffset.UtcNow.AddDays(-3));
        var snag2 = Snag.Report(
            northernOffice.Id, "Ground floor reception", SnagCategory.HeatingAndCooling, SnagSeverity.High,
            "Reception is freezing, heating not working", "U100002", "Tom Brown", DateTimeOffset.UtcNow.AddDays(-1));
        snag2.TransitionTo(SnagStatus.Acknowledged, "U900001", DateTimeOffset.UtcNow.AddHours(-12));
        dbContext.Snags.AddRange(snag1, snag2);

        await dbContext.SaveChangesAsync();
        output.WriteLine("Seed complete.");
    }
}
