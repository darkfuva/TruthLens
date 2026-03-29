namespace TruthLens.Api.Contracts;

public sealed record PagedEventsResponse(
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    IReadOnlyList<EventListItemResponse> Items
);
