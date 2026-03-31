using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TruthLens.Domain.Entities;

namespace TruthLens.Infrastructure.Persistence.Configurations;

public sealed class ExtractedEventCandidateConfiguration : IEntityTypeConfiguration<ExtractedEventCandidate>
{
    public void Configure(EntityTypeBuilder<ExtractedEventCandidate> entity)
    {
        entity.ToTable("extracted_event_candidates");

        entity.HasKey(x => x.Id);

        entity.Property(x => x.Title)
            .HasMaxLength(500)
            .IsRequired();
        entity.Property(x => x.Summary).HasMaxLength(1500);
        entity.Property(x => x.TimeHint).HasMaxLength(200);
        entity.Property(x => x.LocationHint).HasMaxLength(200);
        entity.Property(x => x.Actors).HasMaxLength(1000);
        entity.Property(x => x.Embedding).HasColumnType("vector(768)");
        entity.Property(x => x.ExtractionConfidence).IsRequired();
        entity.Property(x => x.Status)
            .HasMaxLength(30)
            .IsRequired();
        entity.Property(x => x.CreatedAtUtc).IsRequired();

        entity.HasIndex(x => new { x.PostId, x.Status, x.CreatedAtUtc });
        entity.HasIndex(x => x.CreatedAtUtc);
    }
}
