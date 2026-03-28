// CosineSimilarityService.cs
namespace TruthLens.Application.Services.Clustering;

public sealed class CosineSimilarityService : ICosineSimilarityService
{
    public double Calculate(float[] left, float[] right)
    {
        if (left.Length != right.Length || left.Length == 0)
            throw new ArgumentException("Vectors must be same non-zero length.");

        double dot = 0, leftNorm = 0, rightNorm = 0;

        for (var i = 0; i < left.Length; i++)
        {
            dot += left[i] * right[i];
            leftNorm += left[i] * left[i];
            rightNorm += right[i] * right[i];
        }

        if (leftNorm == 0 || rightNorm == 0) return 0;
        return dot / (Math.Sqrt(leftNorm) * Math.Sqrt(rightNorm));
    }
}
