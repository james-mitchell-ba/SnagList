namespace SnagList.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SnagList.Domain.Staff;

public sealed class StaffIdentityConfiguration : IEntityTypeConfiguration<StaffIdentity>
{
    public void Configure(EntityTypeBuilder<StaffIdentity> builder)
    {
        builder.ToTable("staff_identities");
        builder.HasKey(s => s.StaffId);
        builder.Property(s => s.StaffId).HasMaxLength(50);
        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Email).HasMaxLength(300).IsRequired();

        var rolesProperty = builder.Property(s => s.Roles).HasConversion(
            roles => string.Join(',', roles),
            value => value.Length == 0
                ? Array.Empty<StaffRole>()
                : value.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Enum.Parse<StaffRole>).ToArray());
        rolesProperty.Metadata.SetValueComparer(new ValueComparer<IReadOnlyList<StaffRole>>(
            (a, b) => a!.SequenceEqual(b!),
            a => a.Aggregate(0, (hash, role) => HashCode.Combine(hash, role)),
            a => a.ToList()));
    }
}
