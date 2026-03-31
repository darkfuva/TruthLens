// EventSummarizationService.cs
using TruthLens.Application.Repositories.Event;
using TruthLens.Application.Services.Summarization;

namespace TruthLens.Application.Services.Summarization;

public sealed class EventSummarizationService
{
    private readonly IEventRepository _eventRepository;
    private readonly IEventSummarizer _summarizer;

    public EventSummarizationService(IEventRepository eventRepository, IEventSummarizer summarizer)
    {
        _eventRepository = eventRepository;
        _summarizer = summarizer;
    }

    public async Task<int> SummarizePendingEventsAsync(int batchSize, CancellationToken ct)
    {
        var events = await _eventRepository.GetUnsummarizedBatchAsync(batchSize, ct);
        if (events.Count == 0) return 0;

        var updated = 0;
        foreach (var evt in events)
        {
            var posts = await _eventRepository.GetRecentLinkedPostsForEventAsync(evt.Id, 10, ct);
            if (posts.Count == 0)
            {
                var legacyPosts = await _eventRepository.GetRecentPostsForEventAsync(evt.Id, 10, ct);
                posts = legacyPosts
                    .Select(p => (p, new TruthLens.Domain.Entities.PostEventLink
                    {
                        Id = Guid.Empty,
                        EventId = evt.Id,
                        PostId = p.Id,
                        IsPrimary = true,
                        RelevanceScore = p.ClusterAssignmentScore ?? 0.55,
                        RelationType = "PRIMARY_LEGACY"
                    }))
                    .ToList();
            }

            var externalEvidence = await _eventRepository.GetRecentExternalEvidenceForEventAsync(evt.Id, 8, ct);
            if (posts.Count == 0 && externalEvidence.Count == 0) continue;

            var postLines = posts.Select((x, i) =>
                $"{i + 1}. [{(x.Link.IsPrimary ? "PRIMARY" : "SECONDARY")}] score={x.Link.RelevanceScore:F3} " +
                $"title=\"{x.Post.Title}\" source=\"{x.Post.Source?.Name ?? "unknown"}\" " +
                $"published=\"{x.Post.PublishedAtUtc:O}\" url=\"{x.Post.Url}\" " +
                $"summary=\"{x.Post.Summary ?? "unknown"}\"");

            var evidenceLines = externalEvidence.Select((x, i) =>
                $"{i + 1}. score={(x.RelevanceScore ?? 0):F3} title=\"{x.Title}\" " +
                $"domain=\"{x.ExternalSource?.Domain ?? "unknown"}\" url=\"{x.Url}\" " +
                $"published=\"{x.PublishedAtUtc:O}\"");

            var context = $"""
                          EVENT TITLE: {evt.Title}
                          EVENT TIME WINDOW: {evt.FirstSeenAtUtc:O} -> {evt.LastSeenAtUtc:O}
                          INTERNAL EVIDENCE:
                          {string.Join('\n', postLines)}

                          EXTERNAL EVIDENCE:
                          {string.Join('\n', evidenceLines)}

                          CONSTRAINTS:
                          - If evidence conflicts, mention uncertainty.
                          - If key details are missing, explicitly say "unknown".
                          """;

            var summary = await _summarizer.SummarizeAsync(context, ct);

            evt.Summary = summary;
            evt.SummaryModel = "ollama-local";
            evt.SummarizedAtUtc = DateTimeOffset.UtcNow;
            updated++;
        }

        await _eventRepository.SaveChangesAsync(ct);
        return updated;
    }
}
