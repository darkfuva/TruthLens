using TruthLens.Application.Repositories.Event;
using TruthLens.Application.Repositories.Post;
using TruthLens.Application.Services.Extraction;
using TruthLens.Domain.Entities;

namespace TruthLens.Application.Services.Clustering;

public sealed class GraphBackfillService
{
    private readonly IPostRepository _postRepository;
    private readonly IPostEventLinkRepository _postEventLinkRepository;
    private readonly IExtractedEventCandidateRepository _extractedEventCandidateRepository;
    private readonly IEventRepository _eventRepository;
    private readonly IEventCandidateExtractor _eventCandidateExtractor;
    private readonly EventRelationRecomputeService _eventRelationRecomputeService;

    public GraphBackfillService(
        IPostRepository postRepository,
        IPostEventLinkRepository postEventLinkRepository,
        IExtractedEventCandidateRepository extractedEventCandidateRepository,
        IEventRepository eventRepository,
        IEventCandidateExtractor eventCandidateExtractor,
        EventRelationRecomputeService eventRelationRecomputeService)
    {
        _postRepository = postRepository;
        _postEventLinkRepository = postEventLinkRepository;
        _extractedEventCandidateRepository = extractedEventCandidateRepository;
        _eventRepository = eventRepository;
        _eventCandidateExtractor = eventCandidateExtractor;
        _eventRelationRecomputeService = eventRelationRecomputeService;
    }

    public async Task<GraphBackfillResult> BackfillRecentAsync(int lookbackDays, int batchSize, CancellationToken ct)
    {
        var sinceUtc = DateTimeOffset.UtcNow.AddDays(-Math.Max(1, lookbackDays));
        var posts = await _postRepository.GetEmbeddedWithoutPrimaryLinkBatchAsync(Math.Max(1, batchSize), sinceUtc, ct);
        if (posts.Count == 0)
        {
            return new GraphBackfillResult(0, 0, 0, 0);
        }

        var postIds = posts.Select(x => x.Id).ToList();
        var existingLinks = await _postEventLinkRepository.GetForPostIdsAsync(postIds, ct);
        var hasAnyLinkByPost = existingLinks
            .GroupBy(x => x.PostId)
            .ToDictionary(g => g.Key, g => g.Any(l => l.IsPrimary));

        var linksToAdd = new List<PostEventLink>();
        var candidatesToAdd = new List<ExtractedEventCandidate>();
        var touchedEventIds = new HashSet<Guid>();

        foreach (var post in posts)
        {
            if (post.EventId.HasValue && !hasAnyLinkByPost.ContainsKey(post.Id))
            {
                linksToAdd.Add(new PostEventLink
                {
                    Id = Guid.NewGuid(),
                    PostId = post.Id,
                    EventId = post.EventId.Value,
                    IsPrimary = true,
                    RelationType = "PRIMARY_BACKFILL",
                    RelevanceScore = Math.Round(post.ClusterAssignmentScore ?? 1.0, 4),
                    LinkedAtUtc = DateTimeOffset.UtcNow
                });
                touchedEventIds.Add(post.EventId.Value);
            }

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

        if (linksToAdd.Count > 0)
        {
            await _postEventLinkRepository.AddRangeAsync(linksToAdd, ct);
        }

        if (candidatesToAdd.Count > 0)
        {
            await _extractedEventCandidateRepository.AddRangeAsync(candidatesToAdd, ct);
        }

        await _postRepository.SaveChangesAsync(ct);

        if (touchedEventIds.Count > 0)
        {
            await _eventRepository.RecomputeCentroidsFromLinksAsync(touchedEventIds, ct);
            await _eventRepository.SaveChangesAsync(ct);
            await _eventRelationRecomputeService.RecomputeForTouchedEventsAsync(touchedEventIds, ct);
        }

        return new GraphBackfillResult(
            PostsScanned: posts.Count,
            LinksAdded: linksToAdd.Count,
            CandidatesAdded: candidatesToAdd.Count,
            EventsTouched: touchedEventIds.Count);
    }
}
