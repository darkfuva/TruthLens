namespace TruthLens.Domain.Entities;

public sealed class Post
{
    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public DateTimeOffset PublishedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public Source Source { get; set; } = null!;
    public float[]? Embedding { get; set; }
    public string? EmbeddingModel { get; set; }
    public DateTimeOffset? EmbeddedAtUtc { get; set; }
    public Guid? EventId { get; set; }
    public Event? Event { get; set; }
    public double? ClusterAssignmentScore { get; set; }
}
