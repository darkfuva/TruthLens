namespace TruthLens.Domain.Entities;

public sealed class Source
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FeedUrl { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<Post> Posts { get; set; } = new List<Post>();
}
