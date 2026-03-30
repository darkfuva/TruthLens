namespace TruthLens.Api.Contracts;

public sealed record EventPostItemResponse(
    Guid Id,
    string Title,
    string Url,
    DateTimeOffset PublishedAtUtc,
    Guid SourceId,
    string? SourceName
);
