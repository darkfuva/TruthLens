using Pgvector;

namespace TruthLens.Domain.Entities;

public sealed class ExtractedEventCandidate
{
    public Guid Id { get; set; }
    public Guid PostId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? TimeHint { get; set; }
    public string? LocationHint { get; set; }
    public string? Actors { get; set; }
    public Vector? Embedding { get; set; }
    public double ExtractionConfidence { get; set; }
    public string Status { get; set; } = "pending";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public Post Post { get; set; } = null!;
}
