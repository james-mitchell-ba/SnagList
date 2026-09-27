namespace SnagList.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SnagListDbContext>
{
    public SnagListDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<SnagListDbContext>();
        builder.UseNpgsql("Host=localhost;Database=snaglist;Username=postgres;Password=postgres");
        return new SnagListDbContext(builder.Options);
    }
}
