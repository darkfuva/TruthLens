namespace TruthLens.Application.Services.Discovery;

public interface INewsSearchClient
{
    Task<IReadOnlyList<string>> SearchArticleUrlsAsync(string query, int maxResults, CancellationToken ct);
}
