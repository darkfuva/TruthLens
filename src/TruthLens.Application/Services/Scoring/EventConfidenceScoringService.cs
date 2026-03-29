namespace TruthLens.Application.Services.Scoring;


using TruthLens.Application.Repositories.Event;
public sealed class EventConfidenceScoringService
{
    private readonly IEventRepository _eventRepository;
    public EventConfidenceScoringService(IEventRepository eventRepository)
    {
        _eventRepository = eventRepository;
    }
    public async Task<int> RecomputeRecentConfidenceAsync(int batchSize, DateTimeOffset sinceUtc, CancellationToken ct)
    {
        var events = await _eventRepository.GetRecentForConfidenceScoringAsync(batchSize, sinceUtc, ct);
        var now = DateTimeOffset.UtcNow;

        foreach (var evt in events)
        {
            var posts = evt.Posts;
            var externalEvidence = evt.ExternalEvidencePosts;
            if (posts.Count == 0 && externalEvidence.Count == 0)
            {
                evt.ConfidenceScore = 0;
                evt.Status = "provisional";
                evt.ConfirmedAtUtc = null;
                continue;
            }

            var totalEvidenceCount = posts.Count + externalEvidence.Count;
            var postCountScore = Clamp01(totalEvidenceCount / 20.0);

            var internalDomains = posts
                .Where(p => p.Source is not null && !string.IsNullOrWhiteSpace(p.Source.FeedUrl))
                .Select(p => GetDomain(p.Source.FeedUrl))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var externalDomains = externalEvidence
                .Where(x => x.ExternalSource is not null && !string.IsNullOrWhiteSpace(x.ExternalSource.Domain))
                .Select(x => NormalizeDomain(x.ExternalSource.Domain))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var corroboratingSources = internalDomains.Union(externalDomains).Count();
            var sourceDiversityScore = Clamp01(corroboratingSources / 5.0);

            var assignmentQualityScore = Clamp01(
                posts.Where(p => p.ClusterAssignmentScore.HasValue)
                     .Select(p => p.ClusterAssignmentScore!.Value)
                     .DefaultIfEmpty(0.5)
                     .Average());

            var sourceConfidenceScore = Clamp01(
                posts.Select(p => p.Source?.ConfidenceScore ?? 0.5)
                    .DefaultIfEmpty(0.5)
                    .Average());

            var recencyHours = (now - evt.LastSeenAtUtc).TotalHours;
            var recencyScore = Clamp01(1 - (recencyHours / (24 * 7.0))); // decays over 7 days

            var finalScore =
                (0.28 * sourceDiversityScore) +
                (0.20 * postCountScore) +
                (0.22 * assignmentQualityScore) +
                (0.15 * recencyScore) +
                (0.15 * sourceConfidenceScore);

            var isConfirmed = corroboratingSources >= 2 && totalEvidenceCount >= 2;
            evt.Status = isConfirmed ? "confirmed" : "provisional";
            evt.ConfirmedAtUtc = isConfirmed
                ? (evt.ConfirmedAtUtc ?? now)
                : null;

            evt.ConfidenceScore = Math.Round(Clamp01(finalScore), 4);
        }

        await _eventRepository.SaveChangesAsync(ct);
        return events.Count;
    }

    private static double Clamp01(double value) => Math.Max(0, Math.Min(1, value));

    private static string GetDomain(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return string.Empty;
        }

        return NormalizeDomain(uri.Host);
    }

    private static string NormalizeDomain(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized.StartsWith("www.", StringComparison.Ordinal) ? normalized[4..] : normalized;
    }
}
