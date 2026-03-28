using Pgvector;

namespace TruthLens.Application.Services.Clustering;

public interface ICosineSimilarityService
{
    double Calculate(Vector left, Vector right);
}
