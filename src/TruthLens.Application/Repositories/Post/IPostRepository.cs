
namespace TruthLens.Application.Repositories.Post;

using TruthLens.Domain.Entities;

public interface IPostRepository
{
    Task<bool> ExistsAsync(Guid sourceId, string externalId, CancellationToken ct);
    Task AddRangeAsync(IEnumerable<Post> posts, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
    Task<IReadOnlyList<Post>> GetUnembeddedBatchAsync(int batchSize, CancellationToken ct);
    Task<IReadOnlyList<Post>> GetUnclusteredEmbeddedBatchAsync(int batchSize, CancellationToken ct);
}
