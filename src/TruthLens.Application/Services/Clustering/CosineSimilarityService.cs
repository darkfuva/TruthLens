using Pgvector;

// CosineSimilarityService.cs
namespace TruthLens.Application.Services.Clustering;

public sealed class CosineSimilarityService : ICosineSimilarityService
{
    public double Calculate(Vector left, Vector right)
    {
        var leftArray = left.ToArray();
        var rightArray = right.ToArray();

        if (leftArray.Length != rightArray.Length || leftArray.Length == 0)
            throw new ArgumentException("Vectors must be same non-zero length.");

        double dot = 0, leftNorm = 0, rightNorm = 0;

        for (var i = 0; i < leftArray.Length; i++)
        {
            dot += leftArray[i] * rightArray[i];
            leftNorm += leftArray[i] * leftArray[i];
            rightNorm += rightArray[i] * rightArray[i];
        }

        if (leftNorm == 0 || rightNorm == 0) return 0;
        return dot / (Math.Sqrt(leftNorm) * Math.Sqrt(rightNorm));
    }
}
