namespace TruthLens.Domain.Entities;

public sealed class PostEventLink
{
    public Guid Id { get; set; }
    public Guid PostId { get; set; }
    public Guid EventId { get; set; }
    public double RelevanceScore { get; set; }
    public bool IsPrimary { get; set; }
    public string RelationType { get; set; } = "semantic";
    public DateTimeOffset LinkedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public Post Post { get; set; } = null!;
    public Event Event { get; set; } = null!;
}
