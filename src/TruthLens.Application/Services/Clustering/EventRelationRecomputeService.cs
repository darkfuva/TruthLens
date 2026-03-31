using TruthLens.Application.Repositories.Event;
using TruthLens.Domain.Entities;

namespace TruthLens.Application.Services.Clustering;

public sealed class EventRelationRecomputeService
{
    private readonly IEventRepository _eventRepository;
    private readonly IPostEventLinkRepository _postEventLinkRepository;
    private readonly IEventRelationRepository _eventRelationRepository;
    private readonly ICosineSimilarityService _cosineSimilarityService;

    public EventRelationRecomputeService(
        IEventRepository eventRepository,
        IPostEventLinkRepository postEventLinkRepository,
        IEventRelationRepository eventRelationRepository,
        ICosineSimilarityService cosineSimilarityService)
    {
        _eventRepository = eventRepository;
        _postEventLinkRepository = postEventLinkRepository;
        _eventRelationRepository = eventRelationRepository;
        _cosineSimilarityService = cosineSimilarityService;
    }

    public async Task RecomputeForTouchedEventsAsync(IReadOnlyCollection<Guid> touchedEventIds, CancellationToken ct)
    {
        if (touchedEventIds.Count == 0)
        {
            return;
        }

        var candidates = await _eventRepository.GetRecentForRelationRecomputeAsync(
            touchedEventIds,
            DateTimeOffset.UtcNow.AddDays(-14),
            400,
            ct);

        var links = await _postEventLinkRepository.GetForEventIdsAsync(candidates.Select(x => x.Id).ToList(), ct);
        var postIdsByEvent = links
            .GroupBy(x => x.EventId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.PostId).ToHashSet());

        var now = DateTimeOffset.UtcNow;
        var upserts = new List<EventRelation>();

        foreach (var left in candidates.Where(x => touchedEventIds.Contains(x.Id)))
        {
            if (left.CentroidEmbedding is null)
            {
                continue;
            }

            foreach (var right in candidates)
            {
                if (left.Id == right.Id || right.CentroidEmbedding is null)
                {
                    continue;
                }

                var similarity = _cosineSimilarityService.Calculate(left.CentroidEmbedding, right.CentroidEmbedding);
                if (similarity < 0.78)
                {
                    continue;
                }

                postIdsByEvent.TryGetValue(left.Id, out var leftPosts);
                postIdsByEvent.TryGetValue(right.Id, out var rightPosts);
                leftPosts ??= new HashSet<Guid>();
                rightPosts ??= new HashSet<Guid>();

                var sharedCount = leftPosts.Intersect(rightPosts).Count();
                var overlapStrength = Math.Min(1.0, sharedCount / 3.0);
                var strength = Math.Round(Math.Max(0, Math.Min(1, (similarity * 0.75) + (overlapStrength * 0.25))), 4);

                var relation = InferRelation(left, right, similarity, sharedCount, strength, now);
                if (relation is null)
                {
                    continue;
                }

                upserts.Add(relation);
            }
        }

        var deduped = upserts
            .GroupBy(x => new { x.FromEventId, x.ToEventId, x.RelationType })
            .Select(g => g
                .OrderByDescending(x => x.Strength)
                .ThenByDescending(x => x.EvidenceCount)
                .First())
            .ToList();

        await _eventRelationRepository.UpsertRangeAsync(deduped, ct);
        await _eventRelationRepository.RemoveWeakOrStaleForEventIdsAsync(
            touchedEventIds,
            minStrength: 0.72,
            minUpdatedAtUtc: now.AddDays(-14),
            ct);
        await _eventRepository.SaveChangesAsync(ct);
    }

    private static EventRelation? InferRelation(
        Event left,
        Event right,
        double similarity,
        int sharedCount,
        double strength,
        DateTimeOffset now)
    {
        if (similarity >= 0.9 && IsSubEventOf(left, right, sharedCount))
        {
            return new EventRelation
            {
                Id = Guid.NewGuid(),
                FromEventId = left.Id,
                ToEventId = right.Id,
                RelationType = "SUBEVENT_OF",
                Strength = strength,
                EvidenceCount = sharedCount,
                UpdatedAtUtc = now
            };
        }

        if (similarity >= 0.84 && right.FirstSeenAtUtc > left.LastSeenAtUtc.AddHours(-12))
        {
            return new EventRelation
            {
                Id = Guid.NewGuid(),
                FromEventId = left.Id,
                ToEventId = right.Id,
                RelationType = "FOLLOWUP",
                Strength = strength,
                EvidenceCount = sharedCount,
                UpdatedAtUtc = now
            };
        }

        if (similarity >= 0.82)
        {
            var ordered = left.Id.CompareTo(right.Id) <= 0 ? (From: left.Id, To: right.Id) : (From: right.Id, To: left.Id);
            return new EventRelation
            {
                Id = Guid.NewGuid(),
                FromEventId = ordered.From,
                ToEventId = ordered.To,
                RelationType = "RELATED",
                Strength = strength,
                EvidenceCount = sharedCount,
                UpdatedAtUtc = now
            };
        }

        return null;
    }

    private static bool IsSubEventOf(Event left, Event right, int sharedCount)
    {
        var leftWindow = left.LastSeenAtUtc - left.FirstSeenAtUtc;
        var rightWindow = right.LastSeenAtUtc - right.FirstSeenAtUtc;
        if (leftWindow > rightWindow)
        {
            return false;
        }

        var startsInside = left.FirstSeenAtUtc >= right.FirstSeenAtUtc.AddHours(-6);
        var endsInside = left.LastSeenAtUtc <= right.LastSeenAtUtc.AddHours(6);
        return startsInside && endsInside && sharedCount >= 1;
    }
}
