using Microsoft.EntityFrameworkCore;
using SnagList.Infrastructure.Persistence;
using SnagList.SeedData;

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__SnagList")
    ?? throw new InvalidOperationException("ConnectionStrings__SnagList is required.");
var options = new DbContextOptionsBuilder<SnagListDbContext>().UseNpgsql(connectionString).Options;
await using var dbContext = new SnagListDbContext(options);

await SeedRunner.RunAsync(dbContext, Console.Out);
