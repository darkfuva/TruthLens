namespace TruthLens.Application.Services.Discovery;

public interface IFeedUrlDiscoveryClient
{
    Task<IReadOnlyList<string>> DiscoverFeedUrlsAsync(string domain, CancellationToken ct);
}
