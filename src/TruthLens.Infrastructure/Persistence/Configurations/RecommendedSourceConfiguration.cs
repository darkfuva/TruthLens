using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TruthLens.Domain.Entities;

namespace TruthLens.Infrastructure.Persistence.Configurations;

public sealed class RecommendedSourceConfiguration : IEntityTypeConfiguration<RecommendedSource>
{
    public void Configure(EntityTypeBuilder<RecommendedSource> entity)
    {
        entity.ToTable("recommended_sources");

        entity.HasKey(x => x.Id);

        entity.Property(x => x.Name)
            .HasMaxLength(200)
            .IsRequired();

        entity.Property(x => x.Domain)
            .HasMaxLength(300)
            .IsRequired();

        entity.Property(x => x.FeedUrl)
            .HasMaxLength(1000)
            .IsRequired();

        entity.Property(x => x.Topic)
            .HasMaxLength(100);

        entity.Property(x => x.DiscoveryMethod)
            .HasMaxLength(50)
            .IsRequired();

        entity.Property(x => x.Status)
            .HasMaxLength(30)
            .IsRequired();

        entity.Property(x => x.ConfidenceScore);

        entity.Property(x => x.SamplePostCount)
            .HasDefaultValue(0);

        entity.Property(x => x.ReviewNote)
            .HasMaxLength(1000);

        entity.HasIndex(x => x.FeedUrl).IsUnique();
        entity.HasIndex(x => x.Status);
    }
}
