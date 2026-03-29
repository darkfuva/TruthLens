using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TruthLens.Application.Services.Discovery;

namespace TruthLens.Infrastructure.Discovery;

public sealed class FeedUrlDiscoveryClient : IFeedUrlDiscoveryClient
{
    private static readonly ConcurrentDictionary<string, (DateTimeOffset ExpiresAtUtc, IReadOnlyList<string> FeedUrls)> DomainFeedCache =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan DomainFeedCacheTtl = TimeSpan.FromHours(2);

    private static readonly string[] CommonFeedPaths =
    {
        "/feed",
        "/feed/",
        "/rss",
        "/rss.xml",
        "/feed.xml",
        "/atom.xml",
        "/feeds/all.atom.xml",
        "/index.xml",
        "/feeds/posts/default",
        "/news/rss.xml",
        "/blog/rss.xml",
        "/xml/rss/nyt/HomePage.xml",
        "/world/rss.xml"
    };
    private static readonly string[] CommonSections =
    {
        "/",
        "/news",
        "/world",
        "/latest",
        "/topics"
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

        var normalizedDomain = NormalizeDomain(domain);
        if (DomainFeedCache.TryGetValue(normalizedDomain, out var cached) && cached.ExpiresAtUtc > DateTimeOffset.UtcNow)
        {
            return cached.FeedUrls;
        }

        var feeds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var baseUrls = new[] { $"https://{normalizedDomain}" };

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

            foreach (var section in CommonSections)
            {
                var sectionUrl = new Uri(baseUri, section).ToString();
                var sectionHtml = await TryGetStringAsync(sectionUrl, ct);
                if (string.IsNullOrWhiteSpace(sectionHtml))
                {
                    continue;
                }

                foreach (var feedUrl in ExtractFeedLinks(sectionHtml, baseUri))
                {
                    feeds.Add(feedUrl.TrimEnd('/'));
                }
            }

            var sitemapUrl = new Uri(baseUri, "/sitemap.xml").ToString();
            foreach (var sitemapFeed in await ExtractFeedUrlsFromSitemapAsync(sitemapUrl, ct))
            {
                feeds.Add(sitemapFeed.TrimEnd('/'));
            }
        }

        var result = feeds.Take(50).ToList();
        DomainFeedCache[normalizedDomain] = (DateTimeOffset.UtcNow.Add(DomainFeedCacheTtl), result);
        return result;
    }

    private static string NormalizeDomain(string domain)
    {
        var normalized = domain.Trim().ToLowerInvariant();
        return normalized.StartsWith("www.", StringComparison.Ordinal) ? normalized[4..] : normalized;
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

        // Fallback: collect feed-like anchor URLs.
        var hrefMatches = Regex.Matches(html, "href\\s*=\\s*['\\\"](?<href>[^'\\\"]+)['\\\"]", RegexOptions.IgnoreCase);
        foreach (Match hrefMatch in hrefMatches)
        {
            var href = hrefMatch.Groups["href"].Value.Trim();
            if (string.IsNullOrWhiteSpace(href))
            {
                continue;
            }

            if (!Regex.IsMatch(href, "(feed|rss|atom|\\.xml)", RegexOptions.IgnoreCase))
            {
                continue;
            }

            if (Uri.TryCreate(baseUri, href, out var absolute))
            {
                yield return absolute.ToString();
            }
        }
    }

    private async Task<IReadOnlyList<string>> ExtractFeedUrlsFromSitemapAsync(string sitemapUrl, CancellationToken ct)
    {
        var xml = await TryGetStringAsync(sitemapUrl, ct);
        if (string.IsNullOrWhiteSpace(xml))
        {
            return Array.Empty<string>();
        }

        try
        {
            var doc = XDocument.Parse(xml);
            var locElements = doc.Descendants().Where(x => string.Equals(x.Name.LocalName, "loc", StringComparison.OrdinalIgnoreCase));

            return locElements
                .Select(x => (x.Value ?? string.Empty).Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Where(x => Regex.IsMatch(x, "(feed|rss|atom|\\.xml)", RegexOptions.IgnoreCase))
                .Take(50)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
