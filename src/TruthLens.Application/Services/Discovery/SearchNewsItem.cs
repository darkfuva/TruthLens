namespace TruthLens.Application.Services.Discovery;

public sealed record SearchNewsItem(
    string Url,
    string? Title,
    DateTimeOffset? PublishedAtUtc
);
