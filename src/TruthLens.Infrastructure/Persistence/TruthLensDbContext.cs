using Microsoft.EntityFrameworkCore;
using TruthLens.Domain.Entities;

namespace TruthLens.Infrastructure.Persistence;

public sealed class TruthLensDbContext : DbContext
{
    public TruthLensDbContext(DbContextOptions<TruthLensDbContext> options) : base(options) { }

    public DbSet<Source> Sources => Set<Source>();
    public DbSet<Post> Posts => Set<Post>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Source>(entity =>
        {
            entity.ToTable("sources");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.FeedUrl).HasMaxLength(1000).IsRequired();
            entity.HasIndex(x => x.FeedUrl).IsUnique();
        });

        modelBuilder.Entity<Post>(entity =>
        {
            entity.ToTable("posts");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ExternalId).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.Url).HasMaxLength(2000).IsRequired();
            entity.HasOne(x => x.Source)
                .WithMany(x => x.Posts)
                .HasForeignKey(x => x.SourceId);
                entity.Property(x => x.Embedding).HasColumnType("real[]");
            entity.Property(x => x.EmbeddingModel).HasMaxLength(100);
            entity.Property(x => x.EmbeddedAtUtc);
            // Dedup key for one source feed item.
            entity.HasIndex(x => new { x.SourceId, x.ExternalId }).IsUnique();
        });
    }
}
