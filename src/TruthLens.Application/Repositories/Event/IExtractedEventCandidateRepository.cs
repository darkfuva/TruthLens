using TruthLens.Domain.Entities;

namespace TruthLens.Application.Repositories.Event;

public interface IExtractedEventCandidateRepository
{
    Task AddRangeAsync(IEnumerable<ExtractedEventCandidate> candidates, CancellationToken ct);
    Task<IReadOnlyList<ExtractedEventCandidate>> GetRecentForPostAsync(Guid postId, int maxCount, CancellationToken ct);
    Task<IReadOnlyList<ExtractedEventCandidate>> GetPendingBatchAsync(int batchSize, CancellationToken ct);
}
