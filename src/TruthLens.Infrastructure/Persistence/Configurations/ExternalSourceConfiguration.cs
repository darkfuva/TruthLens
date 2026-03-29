using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TruthLens.Domain.Entities;

namespace TruthLens.Infrastructure.Persistence.Configurations;

public sealed class ExternalSourceConfiguration : IEntityTypeConfiguration<ExternalSource>
{
    public void Configure(EntityTypeBuilder<ExternalSource> entity)
    {
        entity.ToTable("external_sources");

        entity.HasKey(x => x.Id);

        entity.Property(x => x.Domain)
            .HasMaxLength(300)
            .IsRequired();

        entity.Property(x => x.Name)
            .HasMaxLength(200)
            .IsRequired();

        entity.Property(x => x.FirstSeenAtUtc).IsRequired();
        entity.Property(x => x.LastSeenAtUtc).IsRequired();

        entity.HasIndex(x => x.Domain).IsUnique();
    }
}
