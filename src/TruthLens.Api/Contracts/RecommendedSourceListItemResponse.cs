namespace TruthLens.Api.Contracts;

public sealed record RecommendedSourceListItemResponse(
    Guid Id,
    string Name,
    string Domain,
    string FeedUrl,
    string? Topic,
    string DiscoveryMethod,
    string Status,
    double? ConfidenceScore,
    int SamplePostCount,
    DateTimeOffset DiscoveredAtUtc,
    DateTimeOffset LastSeenAtUtc,
    DateTimeOffset? ReviewedAtUtc,
    string? ReviewNote
);
