namespace TruthLens.Application.Services.Scoring;


using TruthLens.Application.Repositories.Event;
public sealed class EventConfidenceScoringService
{
    private readonly IEventRepository _eventRepository;
    public EventConfidenceScoringService(IEventRepository eventRepository)
    {
        _eventRepository = eventRepository;
    }
    public async Task<int> RecomputeRecentConfidenceAsync(int batchSize, CancellationToken ct)
    {
        var events = await _eventRepository.GetRecentForConfidenceScoringAsync(batchSize, ct);
        var now = DateTimeOffset.UtcNow;

        foreach (var evt in events)
        {
            var posts = evt.Posts;
            if (posts.Count == 0)
            {
                evt.ConfidenceScore = 0;
                continue;
            }

            var postCountScore = Clamp01(posts.Count / 20.0);
            var sourceDiversityScore = Clamp01(posts.Select(p => p.SourceId).Distinct().Count() / 5.0);

            var assignmentQualityScore = Clamp01(
                posts.Where(p => p.ClusterAssignmentScore.HasValue)
                     .Select(p => p.ClusterAssignmentScore!.Value)
                     .DefaultIfEmpty(0.5)
                     .Average());

            var recencyHours = (now - evt.LastSeenAtUtc).TotalHours;
            var recencyScore = Clamp01(1 - (recencyHours / (24 * 7.0))); // decays over 7 days

            var finalScore =
                (0.35 * sourceDiversityScore) +
                (0.25 * postCountScore) +
                (0.25 * assignmentQualityScore) +
                (0.15 * recencyScore);

            evt.ConfidenceScore = Math.Round(Clamp01(finalScore), 4);
        }

        await _eventRepository.SaveChangesAsync(ct);
        return events.Count;
    }

    private static double Clamp01(double value) => Math.Max(0, Math.Min(1, value));
}
