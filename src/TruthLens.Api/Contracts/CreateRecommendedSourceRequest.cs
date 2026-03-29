using System.ComponentModel.DataAnnotations;

namespace TruthLens.Api.Contracts;

public sealed record CreateRecommendedSourceRequest(
    [property: Required, MaxLength(200)] string Name,
    [property: Required, MaxLength(1000)] string FeedUrl,
    [property: MaxLength(300)] string? Domain,
    [property: MaxLength(100)] string? Topic,
    [property: MaxLength(50)] string? DiscoveryMethod,
    double? ConfidenceScore,
    int? SamplePostCount
);
