namespace SnagList.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SnagList.Domain.Snags;

public sealed class SnagCommentConfiguration : IEntityTypeConfiguration<SnagComment>
{
    public void Configure(EntityTypeBuilder<SnagComment> builder)
    {
        builder.ToTable("snag_comments");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.AuthorStaffId).HasMaxLength(50).IsRequired();
        builder.Property(c => c.AuthorName).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Body).HasMaxLength(4000).IsRequired();
    }
}
