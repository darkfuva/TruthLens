using TruthLens.Domain.Entities;

namespace TruthLens.Application.Repositories.External;

public interface IExternalSourceRepository
{
    Task<IReadOnlyList<ExternalSource>> GetAllAsync(CancellationToken ct);
    Task AddAsync(ExternalSource source, CancellationToken ct);
}
