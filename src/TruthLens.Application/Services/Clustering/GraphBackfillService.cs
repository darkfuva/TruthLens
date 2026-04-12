using TruthLens.Application.Repositories.Event;
using TruthLens.Application.Repositories.Post;
using TruthLens.Application.Services.Extraction;
using TruthLens.Domain.Entities;

namespace TruthLens.Application.Services.Clustering;

public sealed class GraphBackfillService
{
    private readonly IPostRepository _postRepository;
    private readonly IExtractedEventCandidateRepository _extractedEventCandidateRepository;
    private readonly IEventCandidateExtractor _eventCandidateExtractor;

    public GraphBackfillService(
        IPostRepository postRepository,
        IExtractedEventCandidateRepository extractedEventCandidateRepository,
        IEventCandidateExtractor eventCandidateExtractor)
    {
        _postRepository = postRepository;
        _extractedEventCandidateRepository = extractedEventCandidateRepository;
        _eventCandidateExtractor = eventCandidateExtractor;
    }

    public async Task<GraphBackfillResult> BackfillRecentAsync(int lookbackDays, int batchSize, CancellationToken ct)
    {
        var sinceUtc = DateTimeOffset.UtcNow.AddDays(-Math.Max(1, lookbackDays));
        var posts = await _postRepository.GetEmbeddedWithoutPrimaryLinkBatchAsync(Math.Max(1, batchSize), sinceUtc, ct);
        if (posts.Count == 0)
        {
            return new GraphBackfillResult(0, 0, 0, 0);
        }

        var candidatesToAdd = new List<ExtractedEventCandidate>();

        foreach (var post in posts)
        {
            var existingCandidates = await _extractedEventCandidateRepository.GetRecentForPostAsync(post.Id, 1, ct);
            if (existingCandidates.Count > 0)
            {
                continue;
            }

            var drafts = await _eventCandidateExtractor.ExtractAsync(post.Title, post.Summary, ct);
            foreach (var draft in drafts.Take(3))
            {
                candidatesToAdd.Add(new ExtractedEventCandidate
                {
                    Id = Guid.NewGuid(),
                    PostId = post.Id,
                    Title = draft.Title,
                    Summary = draft.Summary,
                    TimeHint = draft.TimeHint,
                    LocationHint = draft.LocationHint,
                    Actors = draft.Actors.Count == 0 ? null : string.Join(", ", draft.Actors),
                    ExtractionConfidence = Math.Round(Math.Max(0, Math.Min(1, draft.Confidence)), 4),
                    Status = draft.HasStrongAnchor ? "qualified" : "weak",
                    CreatedAtUtc = DateTimeOffset.UtcNow
                });
            }
        }

        if (candidatesToAdd.Count > 0)
        {
            await _extractedEventCandidateRepository.AddRangeAsync(candidatesToAdd, ct);
        }

        await _postRepository.SaveChangesAsync(ct);

        return new GraphBackfillResult(
            PostsScanned: posts.Count,
            LinksAdded: 0,
            CandidatesAdded: candidatesToAdd.Count,
            EventsTouched: 0);
    }
}
