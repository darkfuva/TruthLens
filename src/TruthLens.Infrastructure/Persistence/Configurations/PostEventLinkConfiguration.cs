using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TruthLens.Domain.Entities;

namespace TruthLens.Infrastructure.Persistence.Configurations;

public sealed class PostEventLinkConfiguration : IEntityTypeConfiguration<PostEventLink>
{
    public void Configure(EntityTypeBuilder<PostEventLink> entity)
    {
        entity.ToTable("post_event_links");

        entity.HasKey(x => x.Id);

        entity.Property(x => x.RelevanceScore).IsRequired();
        entity.Property(x => x.IsPrimary).IsRequired();
        entity.Property(x => x.RelationType)
            .HasMaxLength(50)
            .IsRequired();
        entity.Property(x => x.LinkedAtUtc).IsRequired();

        entity.HasIndex(x => new { x.PostId, x.EventId }).IsUnique();
        entity.HasIndex(x => x.PostId)
            .IsUnique()
            .HasFilter("\"IsPrimary\" = TRUE")
            .HasDatabaseName("UX_post_event_links_one_primary_per_post");
        entity.HasIndex(x => x.EventId);
        entity.HasIndex(x => new { x.EventId, x.IsPrimary });
        entity.HasIndex(x => x.LinkedAtUtc);
    }
}
