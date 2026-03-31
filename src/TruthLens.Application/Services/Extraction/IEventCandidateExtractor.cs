namespace TruthLens.Application.Services.Extraction;

public interface IEventCandidateExtractor
{
    Task<IReadOnlyList<ExtractedEventCandidateDraft>> ExtractAsync(
        string postTitle,
        string? postSummary,
        CancellationToken ct);
}
