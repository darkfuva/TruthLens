namespace TruthLens.Domain.Entities;

public sealed class RecommendedSource
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string FeedUrl { get; set; } = string.Empty;
    public string? Topic { get; set; }
    public string DiscoveryMethod { get; set; } = "manual";
    public string Status { get; set; } = "pending";
    public double? ConfidenceScore { get; set; }
    public int SamplePostCount { get; set; }
    public DateTimeOffset DiscoveredAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReviewedAtUtc { get; set; }
    public string? ReviewNote { get; set; }
}
