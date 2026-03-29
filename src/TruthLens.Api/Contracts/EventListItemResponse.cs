namespace TruthLens.Api.Contracts;

public sealed record EventListItemResponse(
    Guid Id,
    string Title,
    string? Summary,
    string Status,
    double? ConfidenceScore,
    DateTimeOffset FirstSeenAtUtc,
    DateTimeOffset LastSeenAtUtc,
    int PostCount,
    int ExternalEvidenceCount,
    int TotalEvidenceCount,
    string? LatestPostTitle
);
