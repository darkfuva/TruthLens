using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TruthLens.Domain.Entities;

namespace TruthLens.Infrastructure.Persistence.Configurations;

public sealed class EventRelationConfiguration : IEntityTypeConfiguration<EventRelation>
{
    public void Configure(EntityTypeBuilder<EventRelation> entity)
    {
        entity.ToTable("event_relations");

        entity.HasKey(x => x.Id);

        entity.Property(x => x.RelationType)
            .HasMaxLength(30)
            .IsRequired();
        entity.Property(x => x.Strength).IsRequired();
        entity.Property(x => x.EvidenceCount).IsRequired();
        entity.Property(x => x.UpdatedAtUtc).IsRequired();

        entity.HasIndex(x => new { x.FromEventId, x.ToEventId, x.RelationType }).IsUnique();
        entity.HasIndex(x => new { x.FromEventId, x.UpdatedAtUtc });
        entity.HasIndex(x => new { x.ToEventId, x.UpdatedAtUtc });
    }
}
