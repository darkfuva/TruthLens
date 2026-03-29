using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using TruthLens.Application.Repositories.Event;
using TruthLens.Application.Repositories.External;
using TruthLens.Application.Repositories.Source;
using TruthLens.Application.Services.Rss;
using TruthLens.Domain.Entities;

namespace TruthLens.Application.Services.Discovery;

public sealed class SourceDiscoveryService
{
    private const int PostParallelism = 64;
    private const int FeedValidationParallelism = 64;
    private const int SaveBatchSize = 50;

    private static readonly ConcurrentDictionary<string, (DateTimeOffset ExpiresAtUtc, IReadOnlyList<SearchNewsItem> Hits)> SearchCache =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan SearchCacheTtl = TimeSpan.FromMinutes(45);

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
    private readonly IExternalSourceRepository _externalSourceRepository;
    private readonly IExternalEvidenceRepository _externalEvidenceRepository;
    private readonly INewsSearchClient _newsSearchClient;
    private readonly IFeedUrlDiscoveryClient _feedUrlDiscoveryClient;
    private readonly IRssFeedClient _rssFeedClient;
    private readonly ILogger<SourceDiscoveryService> _logger;
    private readonly bool _interactiveProgressEnabled = !Console.IsOutputRedirected;
    private readonly object _progressLock = new();
    private int _lastProgressLength;
    private DateTimeOffset _lastProgressInfoLogAtUtc = DateTimeOffset.MinValue;
    private int _lastInfoLoggedPostIndex = -1;

    public SourceDiscoveryService(
        IEventRepository eventRepository,
        ISourceRepository sourceRepository,
        IRecommendedSourceRepository recommendedSourceRepository,
        IExternalSourceRepository externalSourceRepository,
        IExternalEvidenceRepository externalEvidenceRepository,
        INewsSearchClient newsSearchClient,
        IFeedUrlDiscoveryClient feedUrlDiscoveryClient,
        IRssFeedClient rssFeedClient,
        ILogger<SourceDiscoveryService> logger)
    {
        _eventRepository = eventRepository;
        _sourceRepository = sourceRepository;
        _recommendedSourceRepository = recommendedSourceRepository;
        _externalSourceRepository = externalSourceRepository;
        _externalEvidenceRepository = externalEvidenceRepository;
        _newsSearchClient = newsSearchClient;
        _feedUrlDiscoveryClient = feedUrlDiscoveryClient;
        _rssFeedClient = rssFeedClient;
        _logger = logger;
    }

    public async Task<SourceDiscoveryRunResult> DiscoverCandidatesAsync(
        int maxEvents,
        int minFeedsPerPost,
        CancellationToken ct)
    {
        var apiMetrics = new DiscoveryApiMetrics();
        var sinceUtc = DateTimeOffset.UtcNow.AddDays(-7);
        var events = await _eventRepository.GetRecentForSourceDiscoveryAsync(sinceUtc, maxEvents, ct);
        var sourceFeedUrls = await _sourceRepository.GetAllFeedUrlsAsync(ct);
        var sourceFeedKeys = sourceFeedUrls
            .Select(BuildFeedKey)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var recommended = await _recommendedSourceRepository.GetAllForDiscoveryAsync(ct);
        var recommendedByFeedKey = BuildRecommendedByFeedKeyMap(recommended);

        var externalSources = await _externalSourceRepository.GetAllAsync(ct);
        var externalSourceByDomain = externalSources.ToDictionary(
            x => NormalizeDomain(x.Domain),
            x => x,
            StringComparer.OrdinalIgnoreCase);

        var workItems = events
            .SelectMany((evt, eventIndex) => evt.Posts
                .Where(p => !string.IsNullOrWhiteSpace(p.Title) || !string.IsNullOrWhiteSpace(p.Url))
                .OrderByDescending(p => p.PublishedAtUtc)
                .Select(post => new DiscoveryWorkItem(evt, post, eventIndex + 1, events.Count)))
            .ToList();

        var totalPosts = workItems.Count;
        if (totalPosts == 0)
        {
            return new SourceDiscoveryRunResult(
                EventsProcessed: events.Count,
                PostsProcessed: 0,
                PostsMeetingTarget: 0,
                CandidatesAdded: 0,
                CandidatesUpdated: 0,
                MinFeedsTarget: minFeedsPerPost);
        }

        var startedAtUtc = DateTimeOffset.UtcNow;
        var completedPosts = 0;
        var postGate = new SemaphoreSlim(PostParallelism, PostParallelism);
        var feedValidationGate = new SemaphoreSlim(FeedValidationParallelism, FeedValidationParallelism);

        RenderProgress(0, totalPosts, startedAtUtc, "starting");

        var computeTasks = workItems.Select(async workItem =>
        {
            await postGate.WaitAsync(ct);
            try
            {
                var result = await DiscoverForPostAsync(
                    workItem,
                    minFeedsPerPost,
                    sourceFeedKeys,
                    feedValidationGate,
                    apiMetrics,
                    ct);

                var done = Interlocked.Increment(ref completedPosts);
                RenderProgress(done, totalPosts, startedAtUtc, $"last post={result.PostId}");
                return result;
            }
            finally
            {
                postGate.Release();
            }
        }).ToArray();

        var computed = await Task.WhenAll(computeTasks);
        CompleteProgressLine();

        var existingEvidenceUrlsByEvent = new Dictionary<Guid, HashSet<string>>();
        foreach (var evt in events)
        {
            var existingEvidenceUrls = await _externalEvidenceRepository.GetExistingUrlsForEventAsync(evt.Id, ct);
            existingEvidenceUrlsByEvent[evt.Id] = new HashSet<string>(existingEvidenceUrls, StringComparer.OrdinalIgnoreCase);
        }

        var candidatesAdded = 0;
        var candidatesUpdated = 0;
        var postsMeetingTarget = 0;
        var postsProcessed = 0;
        var pendingMutations = 0;

        foreach (var result in computed)
        {
            ct.ThrowIfCancellationRequested();
            postsProcessed++;
            if (result.MeetsTarget)
            {
                postsMeetingTarget++;
            }

            var mutated = false;
            if (!existingEvidenceUrlsByEvent.TryGetValue(result.EventId, out var existingEvidenceUrls))
            {
                existingEvidenceUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                existingEvidenceUrlsByEvent[result.EventId] = existingEvidenceUrls;
            }

            foreach (var evidence in result.ExternalEvidenceCandidates)
            {
                if (!existingEvidenceUrls.Add(evidence.Url))
                {
                    continue;
                }

                if (!externalSourceByDomain.TryGetValue(evidence.Domain, out var externalSource))
                {
                    externalSource = new ExternalSource
                    {
                        Id = Guid.NewGuid(),
                        Domain = evidence.Domain,
                        Name = BuildSourceNameFromDomain(evidence.Domain),
                        FirstSeenAtUtc = DateTimeOffset.UtcNow,
                        LastSeenAtUtc = DateTimeOffset.UtcNow
                    };

                    await _externalSourceRepository.AddAsync(externalSource, ct);
                    externalSourceByDomain[evidence.Domain] = externalSource;
                }
                else
                {
                    externalSource.LastSeenAtUtc = DateTimeOffset.UtcNow;
                }

                await _externalEvidenceRepository.AddRangeAsync(
                    new[]
                    {
                        new ExternalEvidencePost
                        {
                            Id = Guid.NewGuid(),
                            EventId = result.EventId,
                            ExternalSourceId = externalSource.Id,
                            Title = evidence.Title,
                            Url = evidence.Url,
                            PublishedAtUtc = evidence.PublishedAtUtc,
                            RelevanceScore = evidence.RelevanceScore,
                            DiscoveredAtUtc = DateTimeOffset.UtcNow
                        }
                    },
                    ct);

                mutated = true;
            }

            foreach (var feed in result.RecommendedFeedCandidates)
            {
                if (sourceFeedKeys.Contains(feed.FeedKey))
                {
                    continue;
                }

                if (recommendedByFeedKey.TryGetValue(feed.FeedKey, out var existingRecommended))
                {
                    existingRecommended.LastSeenAtUtc = DateTimeOffset.UtcNow;
                    existingRecommended.SamplePostCount += 1;
                    existingRecommended.ConfidenceScore = MergeConfidence(existingRecommended.ConfidenceScore, feed.Confidence);
                    candidatesUpdated++;
                    mutated = true;
                    continue;
                }

                var now = DateTimeOffset.UtcNow;
                var recommendedSource = new RecommendedSource
                {
                    Id = Guid.NewGuid(),
                    Name = BuildSourceNameFromDomain(feed.Domain),
                    Domain = feed.Domain,
                    FeedUrl = feed.FeedUrl,
                    Topic = "event-discovery",
                    DiscoveryMethod = "event-search",
                    Status = "pending",
                    ConfidenceScore = feed.Confidence,
                    SamplePostCount = 1,
                    DiscoveredAtUtc = now,
                    LastSeenAtUtc = now
                };

                await _recommendedSourceRepository.AddAsync(recommendedSource, ct);
                recommendedByFeedKey[feed.FeedKey] = recommendedSource;
                candidatesAdded++;
                mutated = true;
            }

            if (mutated)
            {
                pendingMutations++;
            }

            if (pendingMutations >= SaveBatchSize)
            {
                await SaveProgressAsync(ct);
                pendingMutations = 0;
            }
        }

        if (pendingMutations > 0)
        {
            await SaveProgressAsync(ct);
        }

        var metrics = apiMetrics.Snapshot();
        _logger.LogInformation(
            "Discovery API error rates: newsSearch={NewsSearchFailureRate} ({NewsSearchFailures}/{NewsSearchCalls}), feedDiscovery={FeedDiscoveryFailureRate} ({FeedDiscoveryFailures}/{FeedDiscoveryCalls}), feedValidationHttp={FeedValidationFailureRate} ({FeedValidationFailures}/{FeedValidationCalls}), feedValidationRejected={FeedValidationRejectedRate} ({FeedValidationRejected}/{FeedValidationCalls}).",
            FormatRate(metrics.NewsSearchCalls, metrics.NewsSearchFailures),
            metrics.NewsSearchFailures,
            metrics.NewsSearchCalls,
            FormatRate(metrics.FeedDiscoveryCalls, metrics.FeedDiscoveryFailures),
            metrics.FeedDiscoveryFailures,
            metrics.FeedDiscoveryCalls,
            FormatRate(metrics.FeedValidationCalls, metrics.FeedValidationFailures),
            metrics.FeedValidationFailures,
            metrics.FeedValidationCalls,
            FormatRate(metrics.FeedValidationCalls, metrics.FeedValidationRejected),
            metrics.FeedValidationRejected,
            metrics.FeedValidationCalls);

        return new SourceDiscoveryRunResult(
            EventsProcessed: events.Count,
            PostsProcessed: postsProcessed,
            PostsMeetingTarget: postsMeetingTarget,
            CandidatesAdded: candidatesAdded,
            CandidatesUpdated: candidatesUpdated,
            MinFeedsTarget: minFeedsPerPost);
    }

    private async Task<DiscoveryPostResult> DiscoverForPostAsync(
        DiscoveryWorkItem workItem,
        int minFeedsPerPost,
        HashSet<string> sourceFeedKeys,
        SemaphoreSlim feedValidationGate,
        DiscoveryApiMetrics apiMetrics,
        CancellationToken ct)
    {
        try
        {
            var discoveredForPost = 0;
            var seenFeedKeysForPost = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var externalEvidenceCandidates = new List<ExternalEvidenceCandidate>();
            var feedCandidates = new List<RecommendedFeedCandidate>();

            var discoveryData = await DiscoverDomainsAndHitsForPostAsync(workItem.Event, workItem.Post, apiMetrics, ct);
            var discoveryConfidence = ComputeDiscoveryConfidence(workItem.Post);

            foreach (var hit in discoveryData.Hits)
            {
                if (!TryGetDomain(hit.Url, out var hitDomain))
                {
                    continue;
                }

                externalEvidenceCandidates.Add(new ExternalEvidenceCandidate(
                    Domain: hitDomain,
                    Url: hit.Url,
                    Title: string.IsNullOrWhiteSpace(hit.Title) ? "(untitled external evidence)" : hit.Title!,
                    PublishedAtUtc: hit.PublishedAtUtc,
                    RelevanceScore: discoveryConfidence));
            }

            foreach (var domain in discoveryData.Domains)
            {
                if (discoveredForPost >= minFeedsPerPost)
                {
                    break;
                }

                IReadOnlyList<string> candidateFeeds;
                try
                {
                    apiMetrics.IncrementFeedDiscoveryCalls();
                    candidateFeeds = await _feedUrlDiscoveryClient.DiscoverFeedUrlsAsync(domain, ct);
                }
                catch
                {
                    apiMetrics.IncrementFeedDiscoveryFailures();
                    continue;
                }
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

                    if (!seenFeedKeysForPost.Add(feedKey))
                    {
                        continue;
                    }

                    if (sourceFeedKeys.Contains(feedKey))
                    {
                        continue;
                    }

                    if (!await ValidateFeedWithGateAsync(feedUrl, feedValidationGate, apiMetrics, ct))
                    {
                        continue;
                    }

                    feedCandidates.Add(new RecommendedFeedCandidate(
                        Domain: domain,
                        FeedUrl: feedUrl,
                        FeedKey: feedKey,
                        Confidence: discoveryConfidence));
                    discoveredForPost++;
                }
            }

            var uniqueEvidence = externalEvidenceCandidates
                .GroupBy(x => x.Url, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .ToList();

            return new DiscoveryPostResult(
                EventId: workItem.Event.Id,
                PostId: workItem.Post.Id,
                MeetsTarget: discoveredForPost >= minFeedsPerPost,
                ExternalEvidenceCandidates: uniqueEvidence,
                RecommendedFeedCandidates: feedCandidates);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Discovery failed for post {PostId}. Continuing.", workItem.Post.Id);
            return new DiscoveryPostResult(
                EventId: workItem.Event.Id,
                PostId: workItem.Post.Id,
                MeetsTarget: false,
                ExternalEvidenceCandidates: Array.Empty<ExternalEvidenceCandidate>(),
                RecommendedFeedCandidates: Array.Empty<RecommendedFeedCandidate>());
        }
    }

    private async Task<(IReadOnlyCollection<string> Domains, IReadOnlyList<SearchNewsItem> Hits)> DiscoverDomainsAndHitsForPostAsync(
        Event evt,
        Post post,
        DiscoveryApiMetrics apiMetrics,
        CancellationToken ct)
    {
        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hitsByUrl = new Dictionary<string, SearchNewsItem>(StringComparer.OrdinalIgnoreCase);

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
            var cacheKey = query.ToLowerInvariant();
            IReadOnlyList<SearchNewsItem> searchHits;
            if (SearchCache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAtUtc > DateTimeOffset.UtcNow)
            {
                searchHits = cached.Hits;
            }
            else
            {
                try
                {
                    apiMetrics.IncrementNewsSearchCalls();
                    searchHits = await _newsSearchClient.SearchArticleUrlsAsync(query, maxResults: 40, ct);
                }
                catch
                {
                    apiMetrics.IncrementNewsSearchFailures();
                    continue;
                }
                SearchCache[cacheKey] = (DateTimeOffset.UtcNow.Add(SearchCacheTtl), searchHits);
            }

            foreach (var hit in searchHits)
            {
                if (string.IsNullOrWhiteSpace(hit.Url))
                {
                    continue;
                }

                if (!hitsByUrl.ContainsKey(hit.Url))
                {
                    hitsByUrl[hit.Url] = hit;
                }

                if (TryGetDomain(hit.Url, out var domain))
                {
                    domains.Add(domain);
                }
            }
        }

        return (domains, hitsByUrl.Values.ToList());
    }

    private async Task<bool> ValidateFeedWithGateAsync(
        string feedUrl,
        SemaphoreSlim gate,
        DiscoveryApiMetrics apiMetrics,
        CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            return await IsValidFeedAsync(feedUrl, apiMetrics, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task SaveProgressAsync(CancellationToken ct)
    {
        try
        {
            await _recommendedSourceRepository.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Discovery save had a non-fatal failure. Continuing.");
        }
    }

    private async Task<bool> IsValidFeedAsync(string feedUrl, DiscoveryApiMetrics apiMetrics, CancellationToken ct)
    {
        try
        {
            apiMetrics.IncrementFeedValidationCalls();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(6));
            var items = await _rssFeedClient.ReadAsync(feedUrl, timeoutCts.Token);
            var valid = items.Count > 0;
            if (!valid)
            {
                apiMetrics.IncrementFeedValidationRejected();
            }

            return valid;
        }
        catch
        {
            apiMetrics.IncrementFeedValidationFailures();
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

        domain = NormalizeDomain(uri.Host);
        return true;
    }

    private static string BuildSourceNameFromDomain(string domain)
    {
        var trimmed = domain.Replace("www.", "", StringComparison.OrdinalIgnoreCase);
        return trimmed;
    }

    private static string NormalizeDomain(string domain)
    {
        return domain.Trim().ToLowerInvariant().Replace("www.", "", StringComparison.OrdinalIgnoreCase);
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

    private static string BuildProgressBar(int current, int total, int width = 20)
    {
        if (total <= 0)
        {
            return "[no-posts]";
        }

        var ratio = Math.Clamp(current / (double)total, 0d, 1d);
        var filled = (int)Math.Round(ratio * width);
        var bar = new string('#', filled) + new string('-', Math.Max(0, width - filled));
        var percent = (int)Math.Round(ratio * 100);
        return $"[{bar}] {percent}%";
    }

    private void RenderProgress(int postsProcessed, int totalPosts, DateTimeOffset startedAtUtc, string state)
    {
        var ratio = totalPosts <= 0 ? 0d : Math.Clamp(postsProcessed / (double)totalPosts, 0d, 1d);
        var bar = BuildProgressBar(postsProcessed, Math.Max(totalPosts, 1), width: 24);
        var elapsed = DateTimeOffset.UtcNow - startedAtUtc;
        var eta = postsProcessed <= 0
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds((elapsed.TotalSeconds / Math.Max(1, postsProcessed)) * Math.Max(0, totalPosts - postsProcessed));

        var etaText = postsProcessed <= 0 || postsProcessed >= totalPosts ? "--:--" : FormatDuration(eta);
        var text = $"Discovery {bar} posts {postsProcessed}/{totalPosts} elapsed {FormatDuration(elapsed)} eta {etaText} {state}";

        if (!_interactiveProgressEnabled)
        {
            var now = DateTimeOffset.UtcNow;
            if (postsProcessed != _lastInfoLoggedPostIndex || (now - _lastProgressInfoLogAtUtc) >= TimeSpan.FromSeconds(10))
            {
                _lastInfoLoggedPostIndex = postsProcessed;
                _lastProgressInfoLogAtUtc = now;
                _logger.LogInformation("{ProgressLine}", text);
            }

            return;
        }

        lock (_progressLock)
        {
            var padded = text.PadRight(Math.Max(_lastProgressLength, text.Length));
            Console.Write("\r" + padded);
            _lastProgressLength = padded.Length;
        }
    }

    private void CompleteProgressLine()
    {
        if (!_interactiveProgressEnabled)
        {
            return;
        }

        lock (_progressLock)
        {
            Console.WriteLine();
            _lastProgressLength = 0;
        }
    }

    private static string FormatDuration(TimeSpan duration)
    {
        var totalSeconds = Math.Max(0, (int)Math.Round(duration.TotalSeconds));
        var hours = totalSeconds / 3600;
        var minutes = (totalSeconds % 3600) / 60;
        var seconds = totalSeconds % 60;

        if (hours > 0)
        {
            return $"{hours:D2}:{minutes:D2}:{seconds:D2}";
        }

        return $"{minutes:D2}:{seconds:D2}";
    }

    private static string FormatRate(long calls, long failures)
    {
        if (calls <= 0)
        {
            return "n/a";
        }

        var rate = (failures / (double)calls) * 100d;
        return $"{rate:F1}%";
    }

    private sealed record DiscoveryWorkItem(Event Event, Post Post, int EventOrdinal, int TotalEvents);

    private sealed record DiscoveryPostResult(
        Guid EventId,
        Guid PostId,
        bool MeetsTarget,
        IReadOnlyList<ExternalEvidenceCandidate> ExternalEvidenceCandidates,
        IReadOnlyList<RecommendedFeedCandidate> RecommendedFeedCandidates);

    private sealed record ExternalEvidenceCandidate(
        string Domain,
        string Url,
        string Title,
        DateTimeOffset? PublishedAtUtc,
        double RelevanceScore);

    private sealed record RecommendedFeedCandidate(
        string Domain,
        string FeedUrl,
        string FeedKey,
        double Confidence);

    private sealed class DiscoveryApiMetrics
    {
        private long _newsSearchCalls;
        private long _newsSearchFailures;
        private long _feedDiscoveryCalls;
        private long _feedDiscoveryFailures;
        private long _feedValidationCalls;
        private long _feedValidationFailures;
        private long _feedValidationRejected;

        public void IncrementNewsSearchCalls() => Interlocked.Increment(ref _newsSearchCalls);
        public void IncrementNewsSearchFailures() => Interlocked.Increment(ref _newsSearchFailures);
        public void IncrementFeedDiscoveryCalls() => Interlocked.Increment(ref _feedDiscoveryCalls);
        public void IncrementFeedDiscoveryFailures() => Interlocked.Increment(ref _feedDiscoveryFailures);
        public void IncrementFeedValidationCalls() => Interlocked.Increment(ref _feedValidationCalls);
        public void IncrementFeedValidationFailures() => Interlocked.Increment(ref _feedValidationFailures);
        public void IncrementFeedValidationRejected() => Interlocked.Increment(ref _feedValidationRejected);

        public DiscoveryApiMetricsSnapshot Snapshot()
        {
            return new DiscoveryApiMetricsSnapshot(
                NewsSearchCalls: Interlocked.Read(ref _newsSearchCalls),
                NewsSearchFailures: Interlocked.Read(ref _newsSearchFailures),
                FeedDiscoveryCalls: Interlocked.Read(ref _feedDiscoveryCalls),
                FeedDiscoveryFailures: Interlocked.Read(ref _feedDiscoveryFailures),
                FeedValidationCalls: Interlocked.Read(ref _feedValidationCalls),
                FeedValidationFailures: Interlocked.Read(ref _feedValidationFailures),
                FeedValidationRejected: Interlocked.Read(ref _feedValidationRejected));
        }
    }

    private sealed record DiscoveryApiMetricsSnapshot(
        long NewsSearchCalls,
        long NewsSearchFailures,
        long FeedDiscoveryCalls,
        long FeedDiscoveryFailures,
        long FeedValidationCalls,
        long FeedValidationFailures,
        long FeedValidationRejected);
}
