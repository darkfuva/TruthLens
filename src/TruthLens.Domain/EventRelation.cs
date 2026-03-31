namespace TruthLens.Domain.Entities;

public sealed class EventRelation
{
    public Guid Id { get; set; }
    public Guid FromEventId { get; set; }
    public Guid ToEventId { get; set; }
    public string RelationType { get; set; } = "RELATED";
    public double Strength { get; set; }
    public int EvidenceCount { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public Event FromEvent { get; set; } = null!;
    public Event ToEvent { get; set; } = null!;
}
