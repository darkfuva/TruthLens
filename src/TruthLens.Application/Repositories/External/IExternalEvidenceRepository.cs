using TruthLens.Domain.Entities;

namespace TruthLens.Application.Repositories.External;

public interface IExternalEvidenceRepository
{
    Task<IReadOnlyCollection<string>> GetExistingUrlsForEventAsync(Guid eventId, CancellationToken ct);
    Task AddRangeAsync(IEnumerable<ExternalEvidencePost> evidencePosts, CancellationToken ct);
}
