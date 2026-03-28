namespace TruthLens.Application.Services.Summarization;

public interface IEventSummarizer
{
    Task<string> SummarizeAsync(string context, CancellationToken ct);
}
