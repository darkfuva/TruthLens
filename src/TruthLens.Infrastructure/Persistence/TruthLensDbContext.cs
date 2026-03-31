using Microsoft.EntityFrameworkCore;
using TruthLens.Domain.Entities;

namespace TruthLens.Infrastructure.Persistence;

public sealed class TruthLensDbContext : DbContext
{
    public TruthLensDbContext(DbContextOptions<TruthLensDbContext> options) : base(options) { }

    public DbSet<Source> Sources => Set<Source>();
    public DbSet<RecommendedSource> RecommendedSources => Set<RecommendedSource>();
    public DbSet<ExternalSource> ExternalSources => Set<ExternalSource>();
    public DbSet<ExternalEvidencePost> ExternalEvidencePosts => Set<ExternalEvidencePost>();
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<PostEventLink> PostEventLinks => Set<PostEventLink>();
    public DbSet<EventRelation> EventRelations => Set<EventRelation>();
    public DbSet<ExtractedEventCandidate> ExtractedEventCandidates => Set<ExtractedEventCandidate>();

    public DbSet<Event> Events => Set<Event>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TruthLensDbContext).Assembly);
    }
}
