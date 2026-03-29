using TruthLens.Application.Repositories.Event;
using TruthLens.Application.Repositories.Source;
using TruthLens.Application.Services.Rss;
using TruthLens.Domain.Entities;

namespace TruthLens.Application.Services.Discovery;

public sealed class SourceDiscoveryService
{
    private static readonly HashSet<string> IgnoredDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "news.google.com",
        "bing.com",
        "www.bing.com",
        "duckduckgo.com",
        "www.duckduckgo.com"
    };

    private readonly IEventRepository _eventRepository;
    private readonly ISourceRepository _sourceRepository;
    private readonly IRecommendedSourceRepository _recommendedSourceRepository;
    private readonly INewsSearchClient _newsSearchClient;
    private readonly IFeedUrlDiscoveryClient _feedUrlDiscoveryClient;
    private readonly IRssFeedClient _rssFeedClient;

    public SourceDiscoveryService(
        IEventRepository eventRepository,
        ISourceRepository sourceRepository,
        IRecommendedSourceRepository recommendedSourceRepository,
        INewsSearchClient newsSearchClient,
        IFeedUrlDiscoveryClient feedUrlDiscoveryClient,
        IRssFeedClient rssFeedClient)
    {
        _eventRepository = eventRepository;
        _sourceRepository = sourceRepository;
        _recommendedSourceRepository = recommendedSourceRepository;
        _newsSearchClient = newsSearchClient;
        _feedUrlDiscoveryClient = feedUrlDiscoveryClient;
        _rssFeedClient = rssFeedClient;
    }

    public async Task<SourceDiscoveryRunResult> DiscoverCandidatesAsync(
        int maxEvents,
        int minFeedsPerPost,
        CancellationToken ct)
    {
        var sinceUtc = DateTimeOffset.UtcNow.AddDays(-7);
        var events = await _eventRepository.GetRecentForSourceDiscoveryAsync(sinceUtc, maxEvents, ct);
        var sourceFeedUrls = await _sourceRepository.GetAllFeedUrlsAsync(ct);
        var sourceFeedKeys = sourceFeedUrls
            .Select(BuildFeedKey)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var recommended = await _recommendedSourceRepository.GetAllForDiscoveryAsync(ct);
        var recommendedByFeedKey = BuildRecommendedByFeedKeyMap(recommended);

        var candidatesAdded = 0;
        var candidatesUpdated = 0;
        var postsProcessed = 0;
        var postsMeetingTarget = 0;

        foreach (var evt in events)
        {
            ct.ThrowIfCancellationRequested();
            var orderedPosts = evt.Posts
                .Where(p => !string.IsNullOrWhiteSpace(p.Title) || !string.IsNullOrWhiteSpace(p.Url))
                .OrderByDescending(p => p.PublishedAtUtc)
                .ToList();

            foreach (var post in orderedPosts)
            {
                postsProcessed++;
                var discoveredForPost = 0;
                var seenFeedKeysForPost = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var domains = await DiscoverDomainsForPostAsync(evt, post, ct);
                var discoveryConfidence = ComputeDiscoveryConfidence(post);

                foreach (var domain in domains)
                {
                    if (discoveredForPost >= minFeedsPerPost)
                    {
                        break;
                    }

                    var candidateFeeds = await _feedUrlDiscoveryClient.DiscoverFeedUrlsAsync(domain, ct);
                    foreach (var rawFeedUrl in candidateFeeds)
                    {
                        if (discoveredForPost >= minFeedsPerPost)
                        {
                            break;
                        }

                        var feedUrl = NormalizeFeedUrl(rawFeedUrl);
                        var feedKey = BuildFeedKey(feedUrl);
                        if (string.IsNullOrWhiteSpace(feedUrl) || string.IsNullOrWhiteSpace(feedKey))
                        {
                            continue;
                        }

                        // This is critical: never add to recommended if feed is already in sources.
                        if (!seenFeedKeysForPost.Add(feedKey))
                        {
                            continue;
                        }

                        if (sourceFeedKeys.Contains(feedKey))
                        {
                            // Known source: skip recommendation creation and do not consume
                            // the per-post discovery quota, otherwise we stall at low add counts.
                            continue;
                        }

                        if (recommendedByFeedKey.TryGetValue(feedKey, out var existingRecommended))
                        {
                            existingRecommended.LastSeenAtUtc = DateTimeOffset.UtcNow;
                            existingRecommended.SamplePostCount += 1;
                            existingRecommended.ConfidenceScore = MergeConfidence(
                                existingRecommended.ConfidenceScore,
                                discoveryConfidence);
                            candidatesUpdated++;
                            discoveredForPost++;
                            continue;
                        }

                        if (!await IsValidFeedAsync(feedUrl, ct))
                        {
                            continue;
                        }

                        var now = DateTimeOffset.UtcNow;
                        var recommendedSource = new RecommendedSource
                        {
                            Id = Guid.NewGuid(),
                            Name = BuildSourceNameFromDomain(domain),
                            Domain = domain,
                            FeedUrl = feedUrl,
                            Topic = "event-discovery",
                            DiscoveryMethod = "event-search",
                            Status = "pending",
                            ConfidenceScore = discoveryConfidence,
                            SamplePostCount = 1,
                            DiscoveredAtUtc = now,
                            LastSeenAtUtc = now
                        };

                        await _recommendedSourceRepository.AddAsync(recommendedSource, ct);
                        recommendedByFeedKey[feedKey] = recommendedSource;
                        candidatesAdded++;
                        discoveredForPost++;
                    }
                }

                if (discoveredForPost >= minFeedsPerPost)
                {
                    postsMeetingTarget++;
                }
            }
        }

        await _recommendedSourceRepository.SaveChangesAsync(ct);

        return new SourceDiscoveryRunResult(
            EventsProcessed: events.Count,
            PostsProcessed: postsProcessed,
            PostsMeetingTarget: postsMeetingTarget,
            CandidatesAdded: candidatesAdded,
            CandidatesUpdated: candidatesUpdated,
            MinFeedsTarget: minFeedsPerPost);
    }

    private async Task<IReadOnlyCollection<string>> DiscoverDomainsForPostAsync(Event evt, Post post, CancellationToken ct)
    {
        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (TryGetDomain(post.Url, out var sourceDomain))
        {
            domains.Add(sourceDomain);
        }

        var queries = new[]
        {
            post.Title,
            $"{post.Title} {evt.Title}".Trim(),
            evt.Title
        }
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Select(x => x.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

        foreach (var query in queries)
        {
            var articleUrls = await _newsSearchClient.SearchArticleUrlsAsync(query, maxResults: 30, ct);
            foreach (var articleUrl in articleUrls)
            {
                if (TryGetDomain(articleUrl, out var domain))
                {
                    domains.Add(domain);
                }
            }
        }

        return domains;
    }

    private async Task<bool> IsValidFeedAsync(string feedUrl, CancellationToken ct)
    {
        try
        {
            var items = await _rssFeedClient.ReadAsync(feedUrl, ct);
            return items.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetDomain(string? url, out string domain)
    {
        domain = string.Empty;

        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (IgnoredDomains.Contains(uri.Host))
        {
            return false;
        }

        domain = uri.Host.ToLowerInvariant();
        return true;
    }

    private static string BuildSourceNameFromDomain(string domain)
    {
        var trimmed = domain.Replace("www.", "", StringComparison.OrdinalIgnoreCase);
        return trimmed;
    }

    private static string NormalizeFeedUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return string.Empty;
        }

        var cleaned = new UriBuilder(uri)
        {
            Fragment = string.Empty
        }.Uri;

        return cleaned.ToString().TrimEnd('/');
    }

    private static Dictionary<string, RecommendedSource> BuildRecommendedByFeedKeyMap(
        IReadOnlyList<RecommendedSource> recommended)
    {
        var map = new Dictionary<string, RecommendedSource>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in recommended)
        {
            var key = BuildFeedKey(candidate.FeedUrl);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (!map.TryGetValue(key, out var existing) || candidate.LastSeenAtUtc > existing.LastSeenAtUtc)
            {
                map[key] = candidate;
            }
        }

        return map;
    }

    private static string BuildFeedKey(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return string.Empty;
        }

        var host = uri.Host.Trim().ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal))
        {
            host = host[4..];
        }

        var path = uri.AbsolutePath.Trim().ToLowerInvariant().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(path))
        {
            path = "/";
        }

        var query = uri.Query.Trim();
        var port = uri.IsDefaultPort ? string.Empty : $":{uri.Port}";

        return $"{host}{port}{path}{query}";
    }

    private static double ComputeDiscoveryConfidence(Post post)
    {
        var assignmentSignal = Clamp01(post.ClusterAssignmentScore ?? 0.55);
        var ageHours = Math.Max(0, (DateTimeOffset.UtcNow - post.PublishedAtUtc).TotalHours);
        var recencySignal = Math.Exp(-ageHours / 48d); // ~2 days characteristic decay

        return Clamp01((0.75 * assignmentSignal) + (0.25 * recencySignal));
    }

    private static double MergeConfidence(double? existing, double incoming)
    {
        if (!existing.HasValue)
        {
            return Clamp01(incoming);
        }

        return Clamp01((existing.Value * 0.8) + (incoming * 0.2));
    }

    private static double Clamp01(double value)
    {
        if (value < 0d) return 0d;
        if (value > 1d) return 1d;
        return value;
    }
}
