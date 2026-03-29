using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TruthLens.Domain.Entities;

namespace TruthLens.Infrastructure.Persistence.Configurations;

public sealed class ExternalEvidencePostConfiguration : IEntityTypeConfiguration<ExternalEvidencePost>
{
    public void Configure(EntityTypeBuilder<ExternalEvidencePost> entity)
    {
        entity.ToTable("external_evidence_posts");

        entity.HasKey(x => x.Id);

        entity.Property(x => x.Title)
            .HasMaxLength(1000)
            .IsRequired();

        entity.Property(x => x.Url)
            .HasMaxLength(2000)
            .IsRequired();

        entity.Property(x => x.RelevanceScore);
        entity.Property(x => x.DiscoveredAtUtc).IsRequired();

        entity.HasOne(x => x.Event)
            .WithMany(x => x.ExternalEvidencePosts)
            .HasForeignKey(x => x.EventId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(x => x.ExternalSource)
            .WithMany(x => x.EvidencePosts)
            .HasForeignKey(x => x.ExternalSourceId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasIndex(x => new { x.EventId, x.Url }).IsUnique();
        entity.HasIndex(x => x.ExternalSourceId);
        entity.HasIndex(x => x.DiscoveredAtUtc);
    }
}
