namespace SnagList.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using SnagList.Domain.Locations;
using SnagList.Domain.Snags;
using SnagList.Domain.Staff;
using SnagList.Infrastructure.Audit;

public sealed class SnagListDbContext(DbContextOptions<SnagListDbContext> options) : DbContext(options)
{
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Snag> Snags => Set<Snag>();
    public DbSet<StaffIdentity> StaffIdentities => Set<StaffIdentity>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SnagListDbContext).Assembly);
    }
}
