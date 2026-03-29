using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TruthLens.Domain.Entities;

namespace TruthLens.Infrastructure.Persistence.Configurations;

public sealed class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> entity)
    {
        entity.ToTable("events");

        entity.HasKey(x => x.Id);

        entity.Property(x => x.Title)
            .HasMaxLength(500)
            .IsRequired();

        entity.Property(x => x.CentroidEmbedding)
            .HasColumnType("vector(768)");

        entity.Property(x => x.ConfidenceScore);
        entity.Property(x => x.Status)
            .HasMaxLength(30)
            .IsRequired()
            .HasDefaultValue("provisional");
        entity.Property(x => x.ConfirmedAtUtc);

        entity.Property(x => x.FirstSeenAtUtc).IsRequired();
        entity.Property(x => x.LastSeenAtUtc).IsRequired();

        entity.Property(x => x.Summary).HasMaxLength(4000);
        entity.Property(x => x.SummaryModel).HasMaxLength(100);
        entity.Property(x => x.SummarizedAtUtc);

        entity.HasIndex(x => x.LastSeenAtUtc);
        entity.HasIndex(x => x.Status);
    }
}
