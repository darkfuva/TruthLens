namespace TruthLens.Domain.Entities;

public sealed class Source
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FeedUrl { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public double? ConfidenceScore { get; set; }
    public DateTimeOffset? ConfidenceUpdatedAtUtc { get; set; }
    public string? ConfidenceModelVersion { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<Post> Posts { get; set; } = new List<Post>();
}
