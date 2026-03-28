namespace TruthLens.Domain.Entities;

public sealed class Event
{
    public Guid Id { get; set; }

    // Human-readable event label, can be refined later by summarization.
    public string Title { get; set; } = string.Empty;

    // Optional: centroid embedding for fast similarity against new posts.
    public float[]? CentroidEmbedding { get; set; }

    public DateTimeOffset FirstSeenAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAtUtc { get; set; } = DateTimeOffset.UtcNow;

    // Confidence score can be computed later in Week 3.
    public double? ConfidenceScore { get; set; }

    public ICollection<Post> Posts { get; set; } = new List<Post>();
    public string? Summary { get; set; }
    public string? SummaryModel { get; set; }
    public DateTimeOffset? SummarizedAtUtc { get; set; }
}
