namespace SnagList.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SnagList.Domain.Snags;

public sealed class SnagConfiguration : IEntityTypeConfiguration<Snag>
{
    public void Configure(EntityTypeBuilder<Snag> builder)
    {
        builder.ToTable("snags");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.SubLocation).HasMaxLength(300).IsRequired();
        builder.Property(s => s.Description).HasMaxLength(4000).IsRequired();
        builder.Property(s => s.ReportedByStaffId).HasMaxLength(50).IsRequired();
        builder.Property(s => s.ReportedByName).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Category).HasConversion<string>().HasMaxLength(50);
        builder.Property(s => s.Severity).HasConversion<string>().HasMaxLength(50);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(50);
        builder.Property(s => s.Version).IsConcurrencyToken();

        builder.HasIndex(s => s.LocationId);
        builder.HasIndex(s => s.Status);

        builder.OwnsMany(s => s.Photos, photo =>
        {
            photo.ToTable("snag_photos");
            photo.WithOwner().HasForeignKey("snag_id");
            photo.Property<Guid>("id").ValueGeneratedOnAdd();
            photo.HasKey("id");
            photo.Property(p => p.BlobKey).HasColumnName("blob_key").IsRequired();
            photo.Property(p => p.FileName).HasColumnName("file_name").IsRequired();
            photo.Property(p => p.ContentType).HasColumnName("content_type").IsRequired();
            photo.Property(p => p.SizeBytes).HasColumnName("size_bytes");
            photo.Property(p => p.UploadedAt).HasColumnName("uploaded_at");
        });
        builder.Navigation(s => s.Photos).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(s => s.Comments)
            .WithOne()
            .HasForeignKey(c => c.SnagId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(s => s.Comments).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
