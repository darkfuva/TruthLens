namespace TruthLens.Application.Services.Extraction;

public sealed record ExtractedEventCandidateDraft(
    string Title,
    string? Summary,
    string? TimeHint,
    string? LocationHint,
    IReadOnlyList<string> Actors,
    double Confidence,
    string Source)
{
    public bool HasStrongAnchor =>
        !string.IsNullOrWhiteSpace(TimeHint) ||
        !string.IsNullOrWhiteSpace(LocationHint) ||
        (Actors?.Count ?? 0) > 0;
}
