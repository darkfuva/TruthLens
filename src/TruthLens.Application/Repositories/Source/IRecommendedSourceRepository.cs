using TruthLens.Domain.Entities;

namespace TruthLens.Application.Repositories.Source;

public interface IRecommendedSourceRepository
{
    Task<int> CountAsync(string? status, CancellationToken ct);
    Task<IReadOnlyList<RecommendedSource>> GetPageAsync(string? status, int page, int pageSize, CancellationToken ct);
    Task<IReadOnlyList<RecommendedSource>> GetForScoringAsync(int maxCount, CancellationToken ct);
    Task<IReadOnlyList<RecommendedSource>> GetAllForDiscoveryAsync(CancellationToken ct);
    Task<IReadOnlyList<RecommendedSource>> GetPromotableAsync(double minConfidence, int minSamplePostCount, int maxCount, CancellationToken ct);
    Task<RecommendedSource?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<RecommendedSource?> GetByFeedUrlAsync(string feedUrl, CancellationToken ct);
    Task<bool> ExistsByFeedUrlAsync(string feedUrl, CancellationToken ct);
    Task AddAsync(RecommendedSource source, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
