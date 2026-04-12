using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TruthLens.Application.Services.Clustering;
using TruthLens.Infrastructure.Persistence;

namespace TruthLens.Worker;

public sealed class ProvisionalEventGarbageCollectionService
{
    private static readonly Regex TokenSplitRegex = new("[^a-z0-9]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "with", "from", "into", "after", "over", "under", "about", "that", "this", "those",
        "have", "has", "had", "will", "would", "could", "their", "there", "where", "which", "what", "when", "why"
    };

    private readonly TruthLensDbContext _db;
    private readonly ICosineSimilarityService _cosineSimilarityService;
    private readonly EventRelationRecomputeService _eventRelationRecomputeService;
    private readonly ILogger<ProvisionalEventGarbageCollectionService> _logger;

    public ProvisionalEventGarbageCollectionService(
        TruthLensDbContext db,
        ICosineSimilarityService cosineSimilarityService,
        EventRelationRecomputeService eventRelationRecomputeService,
        ILogger<ProvisionalEventGarbageCollectionService> logger)
    {
        _db = db;
        _cosineSimilarityService = cosineSimilarityService;
        _eventRelationRecomputeService = eventRelationRecomputeService;
        _logger = logger;
    }

    public async Task<ProvisionalGcResult> MergeDuplicatesAsync(ProvisionalGcOptions options, CancellationToken ct)
    {
        var sinceUtc = DateTimeOffset.UtcNow.AddDays(-Math.Max(1, options.LookbackDays));
        var maxEvents = Math.Max(10, options.MaxEvents);

        var candidates = await _db.Events
            .Where(e => e.Status == "provisional" && e.CentroidEmbedding != null && e.LastSeenAtUtc >= sinceUtc)
            .OrderByDescending(e => e.LastSeenAtUtc)
            .Take(maxEvents)
            .ToListAsync(ct);

        if (candidates.Count < 2)
        {
            return new ProvisionalGcResult(0, 0, 0, 0, 0, 0, 0, 0);
        }

        var groups = BuildMergeGroups(
            candidates,
            options.SimilarityThreshold,
            options.MaxAgeGapDays,
            options.MinTitleJaccard,
            options.MaxMergeGroupsPerCycle);

        if (groups.Count == 0)
        {
            return new ProvisionalGcResult(candidates.Count, 0, 0, 0, 0, 0, 0, 0);
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var eventIds = candidates.Select(x => x.Id).ToList();
        var postLinkCounts = await _db.PostEventLinks
            .Where(x => eventIds.Contains(x.EventId))
            .GroupBy(x => x.EventId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var mergedEvents = 0;
        var linksMoved = 0;
        var linksDeduped = 0;
        var evidenceMoved = 0;
        var evidenceDeduped = 0;
        var summariesReset = 0;
        var candidatesRequeued = 0;
        var touchedEventIds = new HashSet<Guid>();
        var touchedPostIds = new HashSet<Guid>();
        var desiredPrimaryByPost = new Dictionary<Guid, Guid>();

        foreach (var group in groups)
        {
            ct.ThrowIfCancellationRequested();
            var canonical = SelectCanonical(group, postLinkCounts);
            var loserIds = group.Where(x => x.Id != canonical.Id).Select(x => x.Id).ToList();
            if (loserIds.Count == 0)
            {
                continue;
            }

            var groupEventIds = group.Select(x => x.Id).ToList();
            var groupLinks = await _db.PostEventLinks
                .Where(x => groupEventIds.Contains(x.EventId))
                .ToListAsync(ct);

            var linksByEvent = groupLinks
                .GroupBy(x => x.EventId)
                .ToDictionary(x => x.Key, x => x.ToList());

            var canonicalLinksByPost = linksByEvent.TryGetValue(canonical.Id, out var canonicalLinks)
                ? canonicalLinks.ToDictionary(x => x.PostId, x => x)
                : new Dictionary<Guid, TruthLens.Domain.Entities.PostEventLink>();

            foreach (var loserId in loserIds)
            {
                if (!linksByEvent.TryGetValue(loserId, out var loserLinks))
                {
                    continue;
                }

                foreach (var link in loserLinks)
                {
                    touchedPostIds.Add(link.PostId);
                    if (canonicalLinksByPost.TryGetValue(link.PostId, out var existing))
                    {
                        existing.RelevanceScore = Math.Max(existing.RelevanceScore, link.RelevanceScore);
                        existing.LinkedAtUtc = existing.LinkedAtUtc <= link.LinkedAtUtc ? existing.LinkedAtUtc : link.LinkedAtUtc;
                        _db.PostEventLinks.Remove(link);
                        linksDeduped++;
                        continue;
                    }

                    link.EventId = canonical.Id;
                    link.IsPrimary = false;
                    link.RelationType = "MERGED_DUPLICATE";
                    canonicalLinksByPost[link.PostId] = link;
                    linksMoved++;
                }
            }

            var groupEvidence = await _db.ExternalEvidencePosts
                .Where(x => groupEventIds.Contains(x.EventId))
                .ToListAsync(ct);

            var canonicalEvidenceByUrl = groupEvidence
                .Where(x => x.EventId == canonical.Id)
                .GroupBy(x => x.Url, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var loserId in loserIds)
            {
                foreach (var evidence in groupEvidence.Where(x => x.EventId == loserId))
                {
                    if (canonicalEvidenceByUrl.TryGetValue(evidence.Url, out var existing))
                    {
                        if ((evidence.RelevanceScore ?? 0) > (existing.RelevanceScore ?? 0))
                        {
                            existing.RelevanceScore = evidence.RelevanceScore;
                        }

                        _db.ExternalEvidencePosts.Remove(evidence);
                        evidenceDeduped++;
                        continue;
                    }

                    evidence.EventId = canonical.Id;
                    canonicalEvidenceByUrl[evidence.Url] = evidence;
                    evidenceMoved++;
                }
            }

            var staleRelations = await _db.EventRelations
                .Where(x => loserIds.Contains(x.FromEventId) || loserIds.Contains(x.ToEventId))
                .ToListAsync(ct);
            if (staleRelations.Count > 0)
            {
                _db.EventRelations.RemoveRange(staleRelations);
            }

            canonical.FirstSeenAtUtc = group.Min(x => x.FirstSeenAtUtc);
            canonical.LastSeenAtUtc = group.Max(x => x.LastSeenAtUtc);
            canonical.ConfidenceScore = group.Max(x => x.ConfidenceScore ?? 0);

            canonical.Summary = null;
            canonical.SummaryModel = null;
            canonical.SummarizedAtUtc = null;
            summariesReset++;

            foreach (var loserId in loserIds)
            {
                var loser = group.First(x => x.Id == loserId);
                // Use a tombstone status instead of hard delete to avoid races with
                // concurrent workers (for example discovery inserting evidence).
                loser.Status = "merged";
                loser.CentroidEmbedding = null;
                loser.ConfidenceScore = null;
                loser.ConfirmedAtUtc = null;
                loser.Summary = null;
                loser.SummaryModel = null;
                loser.SummarizedAtUtc = null;
                mergedEvents++;
            }

            touchedEventIds.Add(canonical.Id);
        }

        if (touchedPostIds.Count > 0)
        {
            var affectedLinks = await _db.PostEventLinks
                .Where(x => touchedPostIds.Contains(x.PostId))
                .ToListAsync(ct);

            foreach (var byPost in affectedLinks.GroupBy(x => x.PostId))
            {
                var activeLinks = byPost
                    .Where(x => _db.Entry(x).State != EntityState.Deleted)
                    .ToList();

                var ordered = activeLinks
                    .OrderByDescending(x => x.RelevanceScore)
                    .ThenByDescending(x => x.LinkedAtUtc)
                    .ToList();

                if (ordered.Count == 0)
                {
                    continue;
                }

                var target = ordered[0];
                desiredPrimaryByPost[byPost.Key] = target.Id;

                foreach (var link in ordered)
                {
                    // Phase 1: demote all links. We'll promote exactly one per post in phase 2.
                    link.IsPrimary = false;
                }

                touchedEventIds.UnionWith(ordered.Select(x => x.EventId));
            }

            var postIds = touchedPostIds.ToList();
            var toRequeue = await _db.ExtractedEventCandidates
                .Where(x => postIds.Contains(x.PostId) && x.Status != "pending")
                .ToListAsync(ct);

            foreach (var candidate in toRequeue)
            {
                candidate.Status = "pending";
                candidatesRequeued++;
            }
        }

        // Save phase 1 mutations (including demotions) first, so phase 2 promotions
        // cannot violate one-primary-per-post uniqueness.
        await _db.SaveChangesAsync(ct);

        if (desiredPrimaryByPost.Count > 0)
        {
            var allTargetLinkIds = desiredPrimaryByPost.Values.ToList();
            var targetLinks = await _db.PostEventLinks
                .Where(x => allTargetLinkIds.Contains(x.Id))
                .ToListAsync(ct);

            foreach (var link in targetLinks)
            {
                link.IsPrimary = true;
                if (link.RelationType == "MERGED_DUPLICATE")
                {
                    link.RelationType = "PRIMARY";
                }
            }
        }

        await _db.SaveChangesAsync(ct);

        if (touchedEventIds.Count > 0)
        {
            var touchedList = touchedEventIds.ToList();
            await RecomputeCentroidsForEventsAsync(touchedList, ct);
            await _db.SaveChangesAsync(ct);
            await _eventRelationRecomputeService.RecomputeForTouchedEventsAsync(touchedList, ct);
        }

        await tx.CommitAsync(ct);

        return new ProvisionalGcResult(
            ScannedEvents: candidates.Count,
            MergeGroups: groups.Count,
            MergedEvents: mergedEvents,
            LinksMoved: linksMoved,
            LinksDeduped: linksDeduped,
            EvidenceMoved: evidenceMoved,
            EvidenceDeduped: evidenceDeduped,
            SummariesReset: summariesReset + candidatesRequeued);
    }

    private async Task RecomputeCentroidsForEventsAsync(IReadOnlyCollection<Guid> eventIds, CancellationToken ct)
    {
        if (eventIds.Count == 0)
        {
            return;
        }

        var events = await _db.Events
            .Where(e => eventIds.Contains(e.Id))
            .ToListAsync(ct);

        var links = await _db.PostEventLinks
            .Include(x => x.Post)
            .Where(x => eventIds.Contains(x.EventId) && x.Post.Embedding != null)
            .ToListAsync(ct);

        var byEvent = links.GroupBy(x => x.EventId).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var evt in events)
        {
            if (!byEvent.TryGetValue(evt.Id, out var eventLinks) || eventLinks.Count == 0)
            {
                continue;
            }

            evt.CentroidEmbedding = ComputeWeightedCentroid(eventLinks);
        }
    }

    private static Pgvector.Vector ComputeWeightedCentroid(IReadOnlyList<TruthLens.Domain.Entities.PostEventLink> links)
    {
        var first = links[0].Post.Embedding!.ToArray();
        var dim = first.Length;
        var sums = new double[dim];
        double weightSum = 0;

        foreach (var link in links)
        {
            var embedding = link.Post.Embedding;
            if (embedding is null)
            {
                continue;
            }

            var values = embedding.ToArray();
            if (values.Length != dim)
            {
                continue;
            }

            var weight = Math.Max(0.05, link.RelevanceScore) * (link.IsPrimary ? 1.0 : 0.6);
            weightSum += weight;
            for (var i = 0; i < dim; i++)
            {
                sums[i] += values[i] * weight;
            }
        }

        if (weightSum <= 0)
        {
            return new Pgvector.Vector(first);
        }

        var centroid = new float[dim];
        for (var i = 0; i < dim; i++)
        {
            centroid[i] = (float)(sums[i] / weightSum);
        }

        return new Pgvector.Vector(centroid);
    }

    private List<List<TruthLens.Domain.Entities.Event>> BuildMergeGroups(
        IReadOnlyList<TruthLens.Domain.Entities.Event> events,
        double similarityThreshold,
        int maxAgeGapDays,
        double minTitleJaccard,
        int maxGroups)
    {
        var n = events.Count;
        var parent = new int[n];
        for (var i = 0; i < n; i++)
        {
            parent[i] = i;
        }

        int Find(int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }

            return x;
        }

        void Union(int a, int b)
        {
            var ra = Find(a);
            var rb = Find(b);
            if (ra != rb)
            {
                parent[rb] = ra;
            }
        }

        for (var i = 0; i < n; i++)
        {
            for (var j = i + 1; j < n; j++)
            {
                var left = events[i];
                var right = events[j];
                if (left.CentroidEmbedding is null || right.CentroidEmbedding is null)
                {
                    continue;
                }

                var similarity = _cosineSimilarityService.Calculate(left.CentroidEmbedding, right.CentroidEmbedding);
                if (similarity < similarityThreshold)
                {
                    continue;
                }

                if (Math.Abs((left.LastSeenAtUtc - right.LastSeenAtUtc).TotalDays) > maxAgeGapDays)
                {
                    continue;
                }

                var titleScore = TitleJaccard(left.Title, right.Title);
                if (titleScore < minTitleJaccard && similarity < (similarityThreshold + 0.05))
                {
                    continue;
                }

                Union(i, j);
            }
        }

        var groups = new Dictionary<int, List<TruthLens.Domain.Entities.Event>>();
        for (var i = 0; i < n; i++)
        {
            var root = Find(i);
            if (!groups.TryGetValue(root, out var list))
            {
                list = new List<TruthLens.Domain.Entities.Event>();
                groups[root] = list;
            }

            list.Add(events[i]);
        }

        return groups.Values
            .Where(x => x.Count > 1)
            .OrderByDescending(x => x.Count)
            .Take(Math.Max(1, maxGroups))
            .ToList();
    }

    private static TruthLens.Domain.Entities.Event SelectCanonical(
        IReadOnlyCollection<TruthLens.Domain.Entities.Event> group,
        IReadOnlyDictionary<Guid, int> postLinkCounts)
    {
        return group
            .OrderByDescending(e => postLinkCounts.TryGetValue(e.Id, out var c) ? c : 0)
            .ThenByDescending(e => e.ConfidenceScore ?? 0)
            .ThenByDescending(e => e.LastSeenAtUtc)
            .First();
    }

    private static double TitleJaccard(string left, string right)
    {
        var leftTokens = Tokenize(left);
        var rightTokens = Tokenize(right);
        if (leftTokens.Count == 0 || rightTokens.Count == 0)
        {
            return 0;
        }

        var intersect = leftTokens.Intersect(rightTokens, StringComparer.OrdinalIgnoreCase).Count();
        var union = leftTokens.Union(rightTokens, StringComparer.OrdinalIgnoreCase).Count();
        return union == 0 ? 0 : intersect / (double)union;
    }

    private static HashSet<string> Tokenize(string value)
    {
        var tokens = TokenSplitRegex.Split((value ?? string.Empty).ToLowerInvariant())
            .Where(x => x.Length >= 4 && !StopWords.Contains(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return tokens;
    }
}

public sealed record ProvisionalGcResult(
    int ScannedEvents,
    int MergeGroups,
    int MergedEvents,
    int LinksMoved,
    int LinksDeduped,
    int EvidenceMoved,
    int EvidenceDeduped,
    int SummariesReset);
