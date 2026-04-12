using Microsoft.Extensions.Logging;
using Pgvector;
using TruthLens.Application.Repositories.Event;
using TruthLens.Application.Services.Embedding;
using TruthLens.Domain.Entities;

namespace TruthLens.Application.Services.Clustering;

public sealed class ExtractedEventCandidatePromotionService
{
    private readonly IExtractedEventCandidateRepository _candidateRepository;
    private readonly IPostEventLinkRepository _postEventLinkRepository;
    private readonly IEventRepository _eventRepository;
    private readonly IEmbeddingClient _embeddingClient;
    private readonly ICosineSimilarityService _similarityService;
    private readonly EventRelationRecomputeService _eventRelationRecomputeService;
    private readonly ILogger<ExtractedEventCandidatePromotionService> _logger;

    public ExtractedEventCandidatePromotionService(
        IExtractedEventCandidateRepository candidateRepository,
        IPostEventLinkRepository postEventLinkRepository,
        IEventRepository eventRepository,
        IEmbeddingClient embeddingClient,
        ICosineSimilarityService similarityService,
        EventRelationRecomputeService eventRelationRecomputeService,
        ILogger<ExtractedEventCandidatePromotionService> logger)
    {
        _candidateRepository = candidateRepository;
        _postEventLinkRepository = postEventLinkRepository;
        _eventRepository = eventRepository;
        _embeddingClient = embeddingClient;
        _similarityService = similarityService;
        _eventRelationRecomputeService = eventRelationRecomputeService;
        _logger = logger;
    }

    public async Task<ExtractedCandidatePromotionResult> PromotePendingAsync(
        int batchSize,
        double matchingThreshold,
        int maxLinksPerCandidate,
        int lookbackDays,
        int maxEventCandidates,
        double minCreateConfidence,
        CancellationToken ct)
    {
        var candidates = await _candidateRepository.GetPendingBatchAsync(Math.Max(1, batchSize), ct);
        if (candidates.Count == 0)
        {
            return new ExtractedCandidatePromotionResult(0, 0, 0, 0, 0, 0);
        }

        var now = DateTimeOffset.UtcNow;
        var events = (await _eventRepository.GetRecentWithCentroidAsync(
                now.AddDays(-Math.Max(1, lookbackDays)),
                Math.Max(25, maxEventCandidates),
                ct))
            .Where(x => x.CentroidEmbedding is not null)
            .ToList();

        var postIds = candidates.Select(x => x.PostId).Distinct().ToList();
        var existingLinks = await _postEventLinkRepository.GetForPostIdsAsync(postIds, ct);
        var existingPairSet = existingLinks
            .Select(x => BuildPairKey(x.PostId, x.EventId))
            .ToHashSet(StringComparer.Ordinal);

        await PopulateCandidateEmbeddingsAsync(candidates, ct);

        var newLinks = new List<PostEventLink>();
        var touchedEventIds = new HashSet<Guid>();
        var linksAdded = 0;
        var candidatesLinked = 0;
        var eventsCreated = 0;
        var noMatch = 0;
        var skipped = 0;

        foreach (var candidate in candidates)
        {
            var post = candidate.Post;
            if (post is null || post.Embedding is null)
            {
                candidate.Status = "skipped";
                skipped++;
                continue;
            }

            var candidateEmbedding = candidate.Embedding ?? post.Embedding;
            var topMatches = ScoreEvents(events, candidateEmbedding)
                .Where(x => x.Score >= matchingThreshold)
                .OrderByDescending(x => x.Score)
                .Take(Math.Max(1, maxLinksPerCandidate))
                .ToList();

            var createdForCandidate = false;
            if (topMatches.Count == 0)
            {
                var canPromote = candidate.ExtractionConfidence >= minCreateConfidence ||
                                 string.Equals(candidate.Status, "qualified", StringComparison.OrdinalIgnoreCase);
                if (!canPromote)
                {
                    candidate.Status = "no-match";
                    noMatch++;
                    continue;
                }

                var newEventTitle = !string.IsNullOrWhiteSpace(candidate.Title)
                    ? candidate.Title.Trim()
                    : post.Title;
                var newEvent = await _eventRepository.CreateAsync(newEventTitle, candidateEmbedding, now, ct);
                events.Add(newEvent);
                topMatches.Add((newEvent, 1.0));
                createdForCandidate = true;
                eventsCreated++;
            }

            var addedForCandidate = 0;
            foreach (var (eventEntity, score) in topMatches)
            {
                var pairKey = BuildPairKey(post.Id, eventEntity.Id);
                if (!existingPairSet.Add(pairKey))
                {
                    continue;
                }

                newLinks.Add(new PostEventLink
                {
                    Id = Guid.NewGuid(),
                    PostId = post.Id,
                    EventId = eventEntity.Id,
                    RelevanceScore = Math.Round(Math.Clamp(score, 0d, 1d), 4),
                    // Keep existing primary untouched; this worker adds secondary links.
                    IsPrimary = false,
                    RelationType = createdForCandidate ? "CANDIDATE_PROMOTED" : "CANDIDATE_LINK",
                    LinkedAtUtc = now
                });
                touchedEventIds.Add(eventEntity.Id);
                linksAdded++;
                addedForCandidate++;
            }

            if (addedForCandidate > 0)
            {
                candidatesLinked++;
            }

            candidate.Status = createdForCandidate ? "promoted" : "linked";
        }

        if (newLinks.Count > 0)
        {
            await _postEventLinkRepository.AddRangeAsync(newLinks, ct);
        }

        await _eventRepository.SaveChangesAsync(ct);

        if (touchedEventIds.Count > 0)
        {
            await _eventRepository.RecomputeCentroidsFromLinksAsync(touchedEventIds, ct);
            await _eventRepository.SaveChangesAsync(ct);
            await _eventRelationRecomputeService.RecomputeForTouchedEventsAsync(touchedEventIds, ct);
        }

        return new ExtractedCandidatePromotionResult(
            CandidatesScanned: candidates.Count,
            CandidatesLinked: candidatesLinked,
            EventsCreated: eventsCreated,
            LinksAdded: linksAdded,
            NoMatch: noMatch,
            Skipped: skipped);
    }

    private async Task PopulateCandidateEmbeddingsAsync(
        IReadOnlyList<ExtractedEventCandidate> candidates,
        CancellationToken ct)
    {
        var missing = candidates
            .Where(x => x.Embedding is null && x.Post?.Embedding is not null)
            .ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var texts = missing.Select(BuildCandidateText).ToList();
        try
        {
            var vectors = await _embeddingClient.EmbedAsync(texts, ct);
            if (vectors.Count != texts.Count)
            {
                _logger.LogWarning(
                    "Candidate promotion embedding count mismatch: expected={Expected}, actual={Actual}. Falling back to post embeddings for missing vectors.",
                    texts.Count,
                    vectors.Count);
                return;
            }

            for (var i = 0; i < missing.Count; i++)
            {
                missing[i].Embedding = new Vector(vectors[i]);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Candidate promotion embedding failed. Falling back to post embeddings.");
        }
    }

    private List<(Event Event, double Score)> ScoreEvents(IReadOnlyList<Event> events, Vector candidateEmbedding)
    {
        var scored = new List<(Event Event, double Score)>();
        foreach (var evt in events)
        {
            if (evt.CentroidEmbedding is null)
            {
                continue;
            }

            var score = _similarityService.Calculate(candidateEmbedding, evt.CentroidEmbedding);
            scored.Add((evt, score));
        }

        return scored;
    }

    private static string BuildPairKey(Guid postId, Guid eventId) => $"{postId:N}:{eventId:N}";

    private static string BuildCandidateText(ExtractedEventCandidate candidate)
    {
        var actors = string.IsNullOrWhiteSpace(candidate.Actors) ? string.Empty : $" Actors: {candidate.Actors}.";
        return $"{candidate.Title}. {candidate.Summary ?? string.Empty} Time: {candidate.TimeHint ?? "unknown"}. Location: {candidate.LocationHint ?? "unknown"}.{actors}".Trim();
    }
}

public sealed record ExtractedCandidatePromotionResult(
    int CandidatesScanned,
    int CandidatesLinked,
    int EventsCreated,
    int LinksAdded,
    int NoMatch,
    int Skipped);
