using System.Text.RegularExpressions;
using TruthLens.Application.Services.Discovery;

namespace TruthLens.Infrastructure.Discovery;

public sealed class FeedUrlDiscoveryClient : IFeedUrlDiscoveryClient
{
    private static readonly string[] CommonFeedPaths =
    {
        "/feed",
        "/rss",
        "/rss.xml",
        "/feed.xml",
        "/atom.xml",
        "/feeds/all.atom.xml"
    };

    private readonly HttpClient _httpClient;

    public FeedUrlDiscoveryClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<string>> DiscoverFeedUrlsAsync(string domain, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return Array.Empty<string>();
        }

        var feeds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var baseUrls = new[]
        {
            $"https://{domain.Trim()}",
            $"http://{domain.Trim()}"
        };

        foreach (var baseUrl in baseUrls)
        {
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
            {
                continue;
            }

            // Common feed path heuristics.
            foreach (var path in CommonFeedPaths)
            {
                var candidate = new Uri(baseUri, path).ToString().TrimEnd('/');
                feeds.Add(candidate);
            }

            // Parse alternate feed links from homepage.
            var homepageHtml = await TryGetStringAsync(baseUrl, ct);
            if (string.IsNullOrWhiteSpace(homepageHtml))
            {
                continue;
            }

            foreach (var feedUrl in ExtractFeedLinks(homepageHtml, baseUri))
            {
                feeds.Add(feedUrl.TrimEnd('/'));
            }
        }

        return feeds.Take(20).ToList();
    }

    private async Task<string> TryGetStringAsync(string url, CancellationToken ct)
    {
        try
        {
            using var response = await _httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                return string.Empty;
            }

            return await response.Content.ReadAsStringAsync(ct);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static IEnumerable<string> ExtractFeedLinks(string html, Uri baseUri)
    {
        var linkTagMatches = Regex.Matches(html, "<link\\b[^>]*>", RegexOptions.IgnoreCase);

        foreach (Match match in linkTagMatches)
        {
            var tag = match.Value;

            if (!Regex.IsMatch(tag, "rel\\s*=\\s*['\\\"]?alternate['\\\"]?", RegexOptions.IgnoreCase))
            {
                continue;
            }

            if (!Regex.IsMatch(tag, "type\\s*=\\s*['\\\"]?application\\/(rss\\+xml|atom\\+xml)['\\\"]?", RegexOptions.IgnoreCase))
            {
                continue;
            }

            var hrefMatch = Regex.Match(tag, "href\\s*=\\s*['\\\"](?<href>[^'\\\"]+)['\\\"]", RegexOptions.IgnoreCase);
            if (!hrefMatch.Success)
            {
                continue;
            }

            var href = hrefMatch.Groups["href"].Value.Trim();
            if (string.IsNullOrWhiteSpace(href))
            {
                continue;
            }

            if (Uri.TryCreate(baseUri, href, out var absolute))
            {
                yield return absolute.ToString();
            }
        }
    }
}
