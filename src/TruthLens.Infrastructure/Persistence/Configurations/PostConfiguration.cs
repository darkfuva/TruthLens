using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TruthLens.Domain.Entities;

namespace TruthLens.Infrastructure.Persistence.Configurations;

public sealed class PostConfiguration : IEntityTypeConfiguration<Post>
{
    public void Configure(EntityTypeBuilder<Post> entity)
    {
        entity.ToTable("posts");

        entity.HasKey(x => x.Id);

        entity.Property(x => x.ExternalId)
            .HasMaxLength(1000)
            .IsRequired();

        entity.Property(x => x.Title)
            .HasMaxLength(1000)
            .IsRequired();

        entity.Property(x => x.Url)
            .HasMaxLength(2000)
            .IsRequired();

        entity.Property(x => x.Embedding)
            .HasColumnType("vector(768)");

        entity.Property(x => x.EmbeddingModel)
            .HasMaxLength(100);

        entity.Property(x => x.EmbeddedAtUtc);

        entity.Property(x => x.ClusterAssignmentScore);

        entity.HasOne(x => x.Source)
            .WithMany(x => x.Posts)
            .HasForeignKey(x => x.SourceId);

        entity.HasOne(x => x.Event)
            .WithMany(x => x.Posts)
            .HasForeignKey(x => x.EventId)
            .OnDelete(DeleteBehavior.SetNull);

        entity.HasMany(x => x.EventLinks)
            .WithOne(x => x.Post)
            .HasForeignKey(x => x.PostId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasMany(x => x.ExtractedEventCandidates)
            .WithOne(x => x.Post)
            .HasForeignKey(x => x.PostId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasIndex(x => new { x.SourceId, x.ExternalId })
            .IsUnique();
    }
}
