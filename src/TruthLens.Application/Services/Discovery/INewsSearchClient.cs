namespace TruthLens.Application.Services.Discovery;

public interface INewsSearchClient
{
    Task<IReadOnlyList<SearchNewsItem>> SearchArticleUrlsAsync(string query, int maxResults, CancellationToken ct);
}
