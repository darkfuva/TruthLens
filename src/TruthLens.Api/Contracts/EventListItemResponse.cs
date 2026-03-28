namespace TruthLens.Api.Contracts;

public sealed record EventListItemResponse(
    Guid Id,
    string Title,
    string? Summary,
    double? ConfidenceScore,
    DateTimeOffset FirstSeenAtUtc,
    DateTimeOffset LastSeenAtUtc,
    int PostCount,
    string? LatestPostTitle
);
