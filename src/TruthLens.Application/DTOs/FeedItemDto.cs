namespace TruthLens.Application.DTOs;

public sealed record FeedItemDto(
    string ExternalId,
    string Title,
    string Url,
    string? Summary,
    DateTimeOffset PublishedAtUtc);
