namespace TruthLens.Domain.Entities;

public sealed class ExternalEvidencePost
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public Guid ExternalSourceId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTimeOffset? PublishedAtUtc { get; set; }
    public double? RelevanceScore { get; set; }
    public DateTimeOffset DiscoveredAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public Event Event { get; set; } = null!;
    public ExternalSource ExternalSource { get; set; } = null!;
}
