using TruthLens.Application.Repositories.Source;

namespace TruthLens.Application.Services.Scoring;

public sealed class SourceConfidenceScoringService
{
    private readonly ISourceRepository _sourceRepository;
    private readonly IRecommendedSourceRepository _recommendedSourceRepository;

    public SourceConfidenceScoringService(
        ISourceRepository sourceRepository,
        IRecommendedSourceRepository recommendedSourceRepository)
    {
        _sourceRepository = sourceRepository;
        _recommendedSourceRepository = recommendedSourceRepository;
    }

    public async Task<(int sourcesUpdated, int recommendedUpdated)> RecomputeAsync(
        int maxSources,
        int maxRecommended,
        DateTimeOffset sinceUtc,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var sources = await _sourceRepository.GetActiveForScoringAsync(maxSources, ct);
        var sourceIds = sources.Select(s => s.Id).ToList();
        var statsMap = await _sourceRepository.GetScoringStatsMapAsync(sourceIds, sinceUtc, ct);
        foreach (var source in sources)
        {
            if (!statsMap.TryGetValue(source.Id, out var stats))
            {
                stats = new SourceScoringStats(0, 0, null, null);
            }

            var volumeScore = Clamp01(stats.RecentPostCount / 50.0);
            var corroborationRate = stats.RecentPostCount == 0
                ? 0
                : stats.CorroboratedRecentPostCount / (double)stats.RecentPostCount;
            var corroborationScore = Clamp01(corroborationRate);
            var assignmentScore = Clamp01(stats.AveragePrimaryLinkRelevanceScore ?? 0.5);
            var recencyScore = stats.LatestPublishedAtUtc.HasValue
                ? Clamp01(1 - ((now - stats.LatestPublishedAtUtc.Value).TotalHours / (24 * 14.0)))
                : 0;

            var score =
                (0.30 * volumeScore) +
                (0.30 * corroborationScore) +
                (0.25 * assignmentScore) +
                (0.15 * recencyScore);

            source.ConfidenceScore = Math.Round(Clamp01(score), 4);
            source.ConfidenceUpdatedAtUtc = now;
            source.ConfidenceModelVersion = "source-v1";
        }

        await _sourceRepository.SaveChangesAsync(ct);

        // Score recommended sources using currently known source/domain quality plus metadata.
        var domainConfidence = sources
            .Select(s => (Domain: GetDomain(s.FeedUrl), Score: s.ConfidenceScore ?? 0.5))
            .Where(x => !string.IsNullOrWhiteSpace(x.Domain))
            .GroupBy(x => x.Domain!)
            .ToDictionary(
                g => g.Key,
                g => g.Max(x => x.Score),
                StringComparer.OrdinalIgnoreCase);

        var recommended = await _recommendedSourceRepository.GetForScoringAsync(maxRecommended, ct);
        foreach (var candidate in recommended)
        {
            var domain = !string.IsNullOrWhiteSpace(candidate.Domain)
                ? candidate.Domain.Trim().ToLowerInvariant()
                : GetDomain(candidate.FeedUrl) ?? string.Empty;

            var domainScore = domainConfidence.TryGetValue(domain, out var knownDomainScore)
                ? knownDomainScore
                : 0.4;

            var sampleScore = Clamp01(candidate.SamplePostCount / 20.0);
            var recencyScore = Clamp01(1 - ((now - candidate.LastSeenAtUtc).TotalHours / (24 * 14.0)));

            var score =
                (0.50 * domainScore) +
                (0.30 * sampleScore) +
                (0.20 * recencyScore);

            candidate.ConfidenceScore = Math.Round(Clamp01(score), 4);
        }

        await _recommendedSourceRepository.SaveChangesAsync(ct);

        return (sources.Count, recommended.Count);
    }

    private static double Clamp01(double value) => Math.Max(0, Math.Min(1, value));

    private static string? GetDomain(string feedUrl)
    {
        if (!Uri.TryCreate(feedUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return uri.Host.ToLowerInvariant();
    }
}
