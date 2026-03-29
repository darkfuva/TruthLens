namespace TruthLens.Application.Repositories.Source;

using TruthLens.Domain.Entities;


public interface ISourceRepository
{
    Task<IReadOnlyList<Source>> GetActiveAsync(CancellationToken ct);
}
