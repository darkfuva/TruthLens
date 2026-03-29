namespace TruthLens.Api.Contracts;

public sealed record PagedRecommendedSourcesResponse(
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    IReadOnlyList<RecommendedSourceListItemResponse> Items
);
