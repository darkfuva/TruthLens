// ClusteringService.cs
using Pgvector;
using TruthLens.Application.Repositories.Event;
using TruthLens.Application.Repositories.Post;
using TruthLens.Application.Services.Embedding;
using TruthLens.Application.Services.Extraction;
using TruthLens.Domain.Entities;

namespace TruthLens.Application.Services.Clustering;

public sealed class ClusteringService
{
    private readonly IPostRepository _postRepository;
    private readonly IEventRepository _eventRepository;
    private readonly IPostEventLinkRepository _postEventLinkRepository;
    private readonly IExtractedEventCandidateRepository _extractedEventCandidateRepository;
    private readonly ICosineSimilarityService _similarityService;
    private readonly IEventCandidateExtractor _eventCandidateExtractor;
    private readonly IEmbeddingClient _embeddingClient;
    private readonly EventRelationRecomputeService _eventRelationRecomputeService;

    public ClusteringService(
        IPostRepository postRepository,
        IEventRepository eventRepository,
        IPostEventLinkRepository postEventLinkRepository,
        IExtractedEventCandidateRepository extractedEventCandidateRepository,
        ICosineSimilarityService similarityService,
        IEventCandidateExtractor eventCandidateExtractor,
        IEmbeddingClient embeddingClient,
        EventRelationRecomputeService eventRelationRecomputeService)
    {
        _postRepository = postRepository;
        _eventRepository = eventRepository;
        _postEventLinkRepository = postEventLinkRepository;
        _extractedEventCandidateRepository = extractedEventCandidateRepository;
        _similarityService = similarityService;
        _eventCandidateExtractor = eventCandidateExtractor;
        _embeddingClient = embeddingClient;
        _eventRelationRecomputeService = eventRelationRecomputeService;
    }

    public async Task<int> ClusterPendingPostsAsync(int batchSize, double threshold, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var posts = await _postRepository.GetUnclusteredEmbeddedBatchAsync(batchSize, ct);
        var touchedEventIds = new HashSet<Guid>();
        if (posts.Count == 0) return 0;

        var candidates = (await _eventRepository.GetRecentWithCentroidAsync(now.AddDays(-7), 500, ct))
            .Where(e => e.CentroidEmbedding is not null)
            .ToList();

        var clustered = 0;
        var newLinks = new List<PostEventLink>();
        var newExtractedCandidates = new List<ExtractedEventCandidate>();

        foreach (var post in posts)
        {
            if (post.Embedding is null) continue;

            var extractedDrafts = await _eventCandidateExtractor.ExtractAsync(post.Title, post.Summary, ct);
            if (extractedDrafts.Count == 0)
            {
                extractedDrafts =
                [
                    new ExtractedEventCandidateDraft(
                        Title: post.Title,
                        Summary: post.Summary,
                        TimeHint: null,
                        LocationHint: null,
                        Actors: Array.Empty<string>(),
                        Confidence: 0.5,
                        Source: "fallback")
                ];
            }

            foreach (var draft in extractedDrafts.Take(5))
            {
                newExtractedCandidates.Add(new ExtractedEventCandidate
                {
                    Id = Guid.NewGuid(),
                    PostId = post.Id,
                    Title = draft.Title,
                    Summary = draft.Summary,
                    TimeHint = draft.TimeHint,
                    LocationHint = draft.LocationHint,
                    Actors = draft.Actors.Count == 0 ? null : string.Join(", ", draft.Actors),
                    Embedding = null,
                    ExtractionConfidence = Math.Round(Math.Max(0, Math.Min(1, draft.Confidence)), 4),
                    Status = draft.HasStrongAnchor ? "qualified" : "weak",
                    CreatedAtUtc = now
                });
            }

            var matchInputs = extractedDrafts
                .Where(x => x.HasStrongAnchor)
                .Select(BuildCandidateText)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();

            if (matchInputs.Count == 0)
            {
                matchInputs.Add(BuildCandidateText(new ExtractedEventCandidateDraft(
                    Title: post.Title,
                    Summary: post.Summary,
                    TimeHint: null,
                    LocationHint: null,
                    Actors: Array.Empty<string>(),
                    Confidence: 0.5,
                    Source: "fallback")));
            }

            var inputEmbeddings = await EmbedCandidateInputsAsync(matchInputs, post.Embedding, ct);
            var scoredEvents = ScoreEvents(candidates, inputEmbeddings);
            var topMatches = scoredEvents
                .Where(x => x.Score >= threshold)
                .OrderByDescending(x => x.Score)
                .Take(3)
                .ToList();

            if (topMatches.Count == 0)
            {
                var bestDraft = extractedDrafts
                    .OrderByDescending(x => x.HasStrongAnchor)
                    .ThenByDescending(x => x.Confidence)
                    .First();

                var newEvent = await _eventRepository.CreateAsync(bestDraft.Title, post.Embedding, now, ct);
                candidates.Add(newEvent);

                topMatches.Add((newEvent, 1.0));
            }

            for (var i = 0; i < topMatches.Count; i++)
            {
                var (eventEntity, score) = topMatches[i];
                eventEntity.LastSeenAtUtc = now;

                newLinks.Add(new PostEventLink
                {
                    Id = Guid.NewGuid(),
                    PostId = post.Id,
                    EventId = eventEntity.Id,
                    RelevanceScore = Math.Round(Math.Max(0, Math.Min(1, score)), 4),
                    IsPrimary = i == 0,
                    RelationType = i == 0 ? "PRIMARY" : "SECONDARY",
                    LinkedAtUtc = now
                });

                touchedEventIds.Add(eventEntity.Id);
            }

            var primary = topMatches[0];
            post.EventId = primary.Item1.Id;
            post.ClusterAssignmentScore = primary.Item2;

            clustered++;
        }

        if (newExtractedCandidates.Count > 0)
        {
            var candidatePostIds = newExtractedCandidates
                .Select(x => x.PostId)
                .Distinct()
                .ToList();
            var existingPostIds = await _postRepository.GetExistingIdsAsync(candidatePostIds, ct);

            if (existingPostIds.Count != candidatePostIds.Count)
            {
                newExtractedCandidates = newExtractedCandidates
                    .Where(x => existingPostIds.Contains(x.PostId))
                    .ToList();
            }

            if (newExtractedCandidates.Count > 0)
            {
                await _extractedEventCandidateRepository.AddRangeAsync(newExtractedCandidates, ct);
            }
        }

        if (newLinks.Count > 0)
        {
            var linkPostIds = newLinks
                .Select(x => x.PostId)
                .Distinct()
                .ToList();
            var existingPostIds = await _postRepository.GetExistingIdsAsync(linkPostIds, ct);
            newLinks = newLinks
                .Where(x => existingPostIds.Contains(x.PostId))
                .ToList();

            if (newLinks.Count > 0)
            {
                await _postEventLinkRepository.AddRangeAsync(newLinks, ct);
            }
        }

        await _postRepository.SaveChangesAsync(ct);
        await _eventRepository.RecomputeCentroidsFromLinksAsync(touchedEventIds, ct);
        await _postRepository.SaveChangesAsync(ct);
        await _eventRelationRecomputeService.RecomputeForTouchedEventsAsync(touchedEventIds, ct);
        return clustered;
    }

    private static string BuildCandidateText(ExtractedEventCandidateDraft draft)
    {
        var actors = draft.Actors.Count == 0 ? string.Empty : $"Actors: {string.Join(", ", draft.Actors)}.";
        return $"{draft.Title}. {draft.Summary ?? string.Empty} Time: {draft.TimeHint ?? "unknown"}. Location: {draft.LocationHint ?? "unknown"}. {actors}".Trim();
    }

    private async Task<IReadOnlyList<Vector>> EmbedCandidateInputsAsync(
        IReadOnlyList<string> inputs,
        Vector fallbackEmbedding,
        CancellationToken ct)
    {
        try
        {
            var vectors = await _embeddingClient.EmbedAsync(inputs, ct);
            if (vectors.Count == inputs.Count)
            {
                return vectors.Select(x => new Vector(x)).ToList();
            }
        }
        catch
        {
            // Use fallback embedding for resilience if extraction embeddings fail.
        }

        return inputs.Select(_ => fallbackEmbedding).ToList();
    }

    private List<(Event Event, double Score)> ScoreEvents(IReadOnlyList<Event> candidates, IReadOnlyList<Vector> inputEmbeddings)
    {
        var scored = new List<(Event Event, double Score)>();

        foreach (var evt in candidates)
        {
            if (evt.CentroidEmbedding is null)
            {
                continue;
            }

            var bestScore = double.MinValue;
            foreach (var embedding in inputEmbeddings)
            {
                var score = _similarityService.Calculate(embedding, evt.CentroidEmbedding);
                if (score > bestScore)
                {
                    bestScore = score;
                }
            }

            if (bestScore > double.MinValue)
            {
                scored.Add((evt, bestScore));
            }
        }

        return scored;
    }
}
