namespace TruthLens.Domain.Entities;

public sealed class ExternalSource
{
    public Guid Id { get; set; }
    public string Domain { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset FirstSeenAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAtUtc { get; set; } = DateTimeOffset.UtcNow;

    // Non-RSS evidence links captured from web search results for event corroboration.
    public ICollection<ExternalEvidencePost> EvidencePosts { get; set; } = new List<ExternalEvidencePost>();
}
