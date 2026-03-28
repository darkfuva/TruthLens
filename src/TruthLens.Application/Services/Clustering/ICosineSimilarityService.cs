namespace TruthLens.Application.Services.Clustering;

public interface ICosineSimilarityService
{
    double Calculate(float[] left, float[] right);
}
