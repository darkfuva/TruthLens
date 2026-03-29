using TruthLens.Application.Repositories.Source;
using TruthLens.Domain.Entities;

namespace TruthLens.Application.Services.Discovery;

public sealed class RecommendedSourcePromotionService
{
    private readonly IRecommendedSourceRepository _recommendedSourceRepository;
    private readonly ISourceRepository _sourceRepository;

    public RecommendedSourcePromotionService(
        IRecommendedSourceRepository recommendedSourceRepository,
        ISourceRepository sourceRepository)
    {
        _recommendedSourceRepository = recommendedSourceRepository;
        _sourceRepository = sourceRepository;
    }

    public async Task<int> PromoteQualifiedAsync(
        double minConfidence,
        int minSamplePostCount,
        int maxCount,
        CancellationToken ct)
    {
        var candidates = await _recommendedSourceRepository.GetPromotableAsync(
            minConfidence,
            minSamplePostCount,
            maxCount,
            ct);

        if (candidates.Count == 0)
        {
            return 0;
        }

        var promotedCount = 0;
        var now = DateTimeOffset.UtcNow;

        foreach (var candidate in candidates)
        {
            var alreadyInSources = await _sourceRepository.ExistsByFeedUrlAsync(candidate.FeedUrl, ct);
            if (!alreadyInSources)
            {
                var source = new Source
                {
                    Id = Guid.NewGuid(),
                    Name = candidate.Name,
                    FeedUrl = candidate.FeedUrl,
                    IsActive = true,
                    ConfidenceScore = candidate.ConfidenceScore,
                    ConfidenceUpdatedAtUtc = now,
                    ConfidenceModelVersion = "source-v1-auto-promote"
                };

                await _sourceRepository.AddAsync(source, ct);
                promotedCount++;
            }

            candidate.Status = "promoted";
            candidate.ReviewedAtUtc = now;
            candidate.ReviewNote = "Auto-promoted by worker.";
        }

        // Same scoped DbContext tracks both source additions and candidate updates.
        await _recommendedSourceRepository.SaveChangesAsync(ct);
        return promotedCount;
    }
}
