namespace TruthLens.Application.Repositories.Source;

public sealed record SourceScoringStats(
    int RecentPostCount,
    int CorroboratedRecentPostCount,
    double? AveragePrimaryLinkRelevanceScore,
    DateTimeOffset? LatestPublishedAtUtc
);
