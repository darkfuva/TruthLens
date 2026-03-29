using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TruthLens.Domain.Entities;

namespace TruthLens.Infrastructure.Persistence.Configurations;

public sealed class SourceConfiguration : IEntityTypeConfiguration<Source>
{
    public void Configure(EntityTypeBuilder<Source> entity)
    {
        entity.ToTable("sources");

        entity.HasKey(x => x.Id);

        entity.Property(x => x.Name)
            .HasMaxLength(200)
            .IsRequired();

        entity.Property(x => x.FeedUrl)
            .HasMaxLength(1000)
            .IsRequired();

        entity.Property(x => x.ConfidenceScore);

        entity.Property(x => x.ConfidenceUpdatedAtUtc);

        entity.Property(x => x.ConfidenceModelVersion)
            .HasMaxLength(100);

        entity.HasIndex(x => x.FeedUrl)
            .IsUnique();
    }
}
