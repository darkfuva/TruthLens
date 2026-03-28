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
            var posts = await _eventRepository.GetRecentPostsForEventAsync(evt.Id, 8, ct);
            if (posts.Count == 0) continue;

            var context = string.Join("\n", posts.Select((p, i) => $"{i + 1}. {p.Title} | {p.Summary ?? ""}"));
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
