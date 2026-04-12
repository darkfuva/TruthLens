using Pgvector;

namespace TruthLens.Domain.Entities;

public sealed class Event
{
    public Guid Id { get; set; }

    // Human-readable event label, can be refined later by summarization.
    public string Title { get; set; } = string.Empty;

    // Optional: centroid embedding for fast similarity against new posts.
    public Vector? CentroidEmbedding { get; set; }

    public DateTimeOffset FirstSeenAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAtUtc { get; set; } = DateTimeOffset.UtcNow;

    // Confidence score can be computed later in Week 3.
    public double? ConfidenceScore { get; set; }
    public string Status { get; set; } = "provisional";
    public DateTimeOffset? ConfirmedAtUtc { get; set; }

    public ICollection<PostEventLink> PostLinks { get; set; } = new List<PostEventLink>();
    public ICollection<EventRelation> OutgoingRelations { get; set; } = new List<EventRelation>();
    public ICollection<EventRelation> IncomingRelations { get; set; } = new List<EventRelation>();
    public ICollection<ExternalEvidencePost> ExternalEvidencePosts { get; set; } = new List<ExternalEvidencePost>();
    public string? Summary { get; set; }
    public string? SummaryModel { get; set; }
    public DateTimeOffset? SummarizedAtUtc { get; set; }
}
