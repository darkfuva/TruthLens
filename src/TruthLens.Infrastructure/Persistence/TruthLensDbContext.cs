using Microsoft.EntityFrameworkCore;
using TruthLens.Domain.Entities;

namespace TruthLens.Infrastructure.Persistence;

public sealed class TruthLensDbContext : DbContext
{
    public TruthLensDbContext(DbContextOptions<TruthLensDbContext> options) : base(options) { }

    public DbSet<Source> Sources => Set<Source>();
    public DbSet<RecommendedSource> RecommendedSources => Set<RecommendedSource>();
    public DbSet<Post> Posts => Set<Post>();

    public DbSet<Event> Events => Set<Event>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TruthLensDbContext).Assembly);
    }
}
