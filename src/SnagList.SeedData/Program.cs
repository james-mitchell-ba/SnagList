using Microsoft.EntityFrameworkCore;
using SnagList.Infrastructure.Persistence;
using SnagList.SeedData;
using SnagList.SeedData.Keycloak;

var reseed = args.Contains("--reseed");

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__SnagList")
    ?? throw new InvalidOperationException("ConnectionStrings__SnagList is required.");
var dbOptions = new DbContextOptionsBuilder<SnagListDbContext>().UseNpgsql(connectionString).Options;
await using var dbContext = new SnagListDbContext(dbOptions);

await SeedRunner.RunAsync(dbContext, Console.Out, reseed);

if (Environment.GetEnvironmentVariable("Keycloak__ManageRealm") == "true")
{
    var adminOptions = new KeycloakAdminOptions
    {
        AdminUrl = Environment.GetEnvironmentVariable("Keycloak__AdminUrl")
            ?? throw new InvalidOperationException("Keycloak__AdminUrl is required."),
        AdminUsername = Environment.GetEnvironmentVariable("Keycloak__AdminUsername")
            ?? throw new InvalidOperationException("Keycloak__AdminUsername is required."),
        AdminPassword = Environment.GetEnvironmentVariable("Keycloak__AdminPassword")
            ?? throw new InvalidOperationException("Keycloak__AdminPassword is required."),
    };
    var realmExportPath = Environment.GetEnvironmentVariable("Keycloak__RealmExportPath")
        ?? throw new InvalidOperationException("Keycloak__RealmExportPath is required.");
    var realmExportJson = await File.ReadAllTextAsync(realmExportPath);

    Console.WriteLine(reseed ? "Reseeding the Keycloak realm..." : "Converging the Keycloak realm...");
    await new KeycloakRealmConverger(new HttpClient(), adminOptions).ConvergeAsync(realmExportJson, reseed, default);
    Console.WriteLine("Keycloak realm convergence complete.");
}
