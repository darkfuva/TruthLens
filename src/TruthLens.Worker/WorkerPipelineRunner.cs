using Microsoft.EntityFrameworkCore;
using TruthLens.Application.Services.Clustering;
using TruthLens.Application.Services.Discovery;
using TruthLens.Application.Services.Embedding;
using TruthLens.Application.Services.Rss;
using TruthLens.Application.Services.Scoring;
using TruthLens.Application.Services.Summarization;
using TruthLens.Infrastructure.Persistence;

namespace TruthLens.Worker;

public sealed class WorkerPipelineRunner
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WorkerPipelineRunner> _logger;

    public WorkerPipelineRunner(
        IServiceScopeFactory scopeFactory,
        ILogger<WorkerPipelineRunner> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    // Each method below represents an independent job. We split jobs so slow
    // tasks (discovery/scoring/summarization) do not block ingest+cluster.
    public async Task RunIngestionEmbeddingClusteringCycleAsync(IngestionOptions options, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var ingestionService = scope.ServiceProvider.GetRequiredService<RssIngestionService>();
        var embeddingService = scope.ServiceProvider.GetRequiredService<EmbeddingGenerationService>();
        var clusteringService = scope.ServiceProvider.GetRequiredService<ClusteringService>();
        var db = scope.ServiceProvider.GetRequiredService<TruthLensDbContext>();

        var insertedCount = await ingestionService.IngestAllAsync(ct);
        _logger.LogInformation("Ingestion cycle: inserted {InsertedCount} posts.", insertedCount);

        var maxIterations = Math.Max(1, options.MaxIterations);
        var lastPendingEmbedding = int.MaxValue;
        var lastPendingLinking = int.MaxValue;

        for (var iteration = 1; iteration <= maxIterations; iteration++)
        {
            ct.ThrowIfCancellationRequested();
            var pendingLinkingBefore = await db.Posts.CountAsync(
                p => p.Embedding != null && !p.EventLinks.Any(l => l.IsPrimary),
                ct);
            var embeddedCount = 0;
            try
            {
                embeddedCount = await embeddingService.GenerateForPendingPostsAsync(
                    Math.Max(1, options.EmbeddingBatchSize),
                    ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Embedding step failed in ingestion cycle iteration {Iteration}. Continuing to clustering.", iteration);
            }

            var clusteredCount = 0;
            try
            {
                clusteredCount = await clusteringService.ClusterPendingPostsAsync(
                    Math.Max(1, options.ClusteringBatchSize),
                    options.ClusteringThreshold,
                    ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Clustering/linking step failed in ingestion cycle iteration {Iteration}.", iteration);
            }


            var pendingEmbedding = await db.Posts.CountAsync(p => p.Embedding == null, ct);
            var pendingLinking = await db.Posts.CountAsync(
                p => p.Embedding != null && !p.EventLinks.Any(l => l.IsPrimary),
                ct);
            var postsWithDuplicatePrimary = await db.PostEventLinks
                .Where(x => x.IsPrimary)
                .GroupBy(x => x.PostId)
                .CountAsync(g => g.Count() > 1, ct);

            var linkedThisIteration = Math.Max(0, pendingLinkingBefore - pendingLinking);
            var linkerFailures = Math.Max(0, clusteredCount - linkedThisIteration);
            var linkerSuccessRate = clusteredCount <= 0
                ? "n/a"
                : $"{(linkedThisIteration / (double)clusteredCount) * 100d:F1}%";

            _logger.LogInformation(
                "Ingestion cycle iteration {Iteration}: embedded={EmbeddedCount}, clustered={ClusteredCount}, pendingEmbedding={PendingEmbedding}, pendingLinking={PendingLinking}, duplicatePrimaryPosts={DuplicatePrimaryPosts}, linkerLinked={LinkerLinked}, linkerFailed={LinkerFailed}, linkerSuccessRate={LinkerSuccessRate}.",
                iteration,
                embeddedCount,
                clusteredCount,
                pendingEmbedding,
                pendingLinking,
                postsWithDuplicatePrimary,
                linkedThisIteration,
                linkerFailures,
                linkerSuccessRate);

            if (pendingLinking > pendingLinkingBefore)
            {
                _logger.LogWarning(
                    "Link backlog grew during ingestion iteration {Iteration}: before={Before}, after={After}.",
                    iteration,
                    pendingLinkingBefore,
                    pendingLinking);
            }

            if (pendingEmbedding == 0 && pendingLinking == 0)
            {
                break;
            }

            if (pendingEmbedding == lastPendingEmbedding && pendingLinking == lastPendingLinking)
            {
                _logger.LogWarning("Ingestion cycle stopped due to no progress at iteration {Iteration}.", iteration);
                break;
            }

            lastPendingEmbedding = pendingEmbedding;
            lastPendingLinking = pendingLinking;
        }
    }

    public async Task RunDiscoveryPromotionCycleAsync(
        DiscoveryOptions options,
        IngestionOptions ingestionOptions,
        CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();

        var discoveryService = scope.ServiceProvider.GetRequiredService<SourceDiscoveryService>();
        var promotionService = scope.ServiceProvider.GetRequiredService<RecommendedSourcePromotionService>();
        var ingestionService = scope.ServiceProvider.GetRequiredService<RssIngestionService>();
        var embeddingService = scope.ServiceProvider.GetRequiredService<EmbeddingGenerationService>();
        var clusteringService = scope.ServiceProvider.GetRequiredService<ClusteringService>();
        var db = scope.ServiceProvider.GetRequiredService<TruthLensDbContext>();

        var discoveryResult = await discoveryService.DiscoverCandidatesAsync(
            maxEvents: Math.Max(1, options.MaxEvents),
            minFeedsPerPost: Math.Max(1, options.MinFeedsPerPost),
            ct);

        _logger.LogInformation(
            "Discovery cycle: events={EventsProcessed}, posts={PostsProcessed}, postsMeetingTarget={PostsMeetingTarget}, added={Added}, updated={Updated}, targetPerPost={Target}.",
            discoveryResult.EventsProcessed,
            discoveryResult.PostsProcessed,
            discoveryResult.PostsMeetingTarget,
            discoveryResult.CandidatesAdded,
            discoveryResult.CandidatesUpdated,
            discoveryResult.MinFeedsTarget);

        if (!options.AutoPromoteEnabled)
        {
            _logger.LogInformation("Discovery cycle: auto-promotion disabled.");
            return;
        }

        var autoPromotedCount = await promotionService.PromoteQualifiedAsync(
            minConfidence: options.AutoPromoteMinConfidence,
            minSamplePostCount: Math.Max(1, options.AutoPromoteMinSamplePostCount),
            maxCount: Math.Max(1, options.AutoPromoteMaxCount),
            ct);
        _logger.LogInformation("Discovery cycle: auto-promoted {Count} recommended sources.", autoPromotedCount);

        if (autoPromotedCount == 0)
        {
            return;
        }

        var insertedAfterPromotion = await ingestionService.IngestAllAsync(ct);
        _logger.LogInformation("Discovery cycle: post-promotion ingestion inserted {InsertedCount}.", insertedAfterPromotion);

        var lastPendingEmbedding = int.MaxValue;
        var lastPendingLinking = int.MaxValue;
        var maxCatchupIterations = Math.Max(1, options.PostPromotionMaxCatchupIterations);

        for (var iteration = 1; iteration <= maxCatchupIterations; iteration++)
        {
            var pendingLinkingBefore = await db.Posts.CountAsync(
                p => p.Embedding != null && !p.EventLinks.Any(l => l.IsPrimary),
                ct);
            var embedded = 0;
            try
            {
                embedded = await embeddingService.GenerateForPendingPostsAsync(Math.Max(1, ingestionOptions.EmbeddingBatchSize), ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Embedding step failed in ingestion cycle iteration {Iteration}. Continuing to clustering.", iteration);
            }
            var clustered = 0;
            try
            {
                clustered = await clusteringService.ClusterPendingPostsAsync(
                    Math.Max(1, ingestionOptions.ClusteringBatchSize),
                    ingestionOptions.ClusteringThreshold,
                    ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Clustering/linking step failed in ingestion cycle iteration {Iteration}.", iteration);
            }

            var pendingEmbedding = await db.Posts.CountAsync(p => p.Embedding == null, ct);
            var pendingLinking = await db.Posts.CountAsync(
                  p => p.Embedding != null && !p.EventLinks.Any(l => l.IsPrimary),
              ct);
            var postsWithDuplicatePrimary = await db.PostEventLinks
                .Where(x => x.IsPrimary)
                .GroupBy(x => x.PostId)
                .CountAsync(g => g.Count() > 1, ct);
            var linkedThisIteration = Math.Max(0, pendingLinkingBefore - pendingLinking);
            var linkerFailures = Math.Max(0, clustered - linkedThisIteration);
            var linkerSuccessRate = clustered <= 0
                ? "n/a"
                : $"{(linkedThisIteration / (double)clustered) * 100d:F1}%";

            _logger.LogInformation(
                "Discovery post-promotion iteration {Iteration}: embedded={Embedded}, clustered={Clustered}, pendingEmbedding={PendingEmbedding}, pendingLinking={PendingLinking}, duplicatePrimaryPosts={DuplicatePrimaryPosts}, linkerLinked={LinkerLinked}, linkerFailed={LinkerFailed}, linkerSuccessRate={LinkerSuccessRate}.",
                iteration,
                embedded,
                clustered,
                pendingEmbedding,
                pendingLinking,
                postsWithDuplicatePrimary,
                linkedThisIteration,
                linkerFailures,
                linkerSuccessRate);

            if (pendingLinking > pendingLinkingBefore)
            {
                _logger.LogWarning(
                    "Link backlog grew during discovery post-promotion iteration {Iteration}: before={Before}, after={After}.",
                    iteration,
                    pendingLinkingBefore,
                    pendingLinking);
            }

            if (pendingEmbedding == 0 && pendingLinking == 0)
            {
                break;
            }

            if (pendingEmbedding == lastPendingEmbedding && pendingLinking == lastPendingLinking)
            {
                _logger.LogWarning("Discovery post-promotion catch-up stopped due to no progress at iteration {Iteration}.", iteration);
                break;
            }

            lastPendingEmbedding = pendingEmbedding;
            lastPendingLinking = pendingLinking;
        }
    }

    public async Task RunScoringCycleAsync(ScoringOptions options, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var sourceConfidenceService = scope.ServiceProvider.GetRequiredService<SourceConfidenceScoringService>();
        var eventConfidenceService = scope.ServiceProvider.GetRequiredService<EventConfidenceScoringService>();
        var sinceUtc = DateTimeOffset.UtcNow.AddDays(-Math.Max(1, options.LookbackDays));

        var sourceScoring = await sourceConfidenceService.RecomputeAsync(
            maxSources: Math.Max(1, options.MaxSources),
            maxRecommended: Math.Max(1, options.MaxRecommended),
            sinceUtc: sinceUtc,
            ct);
        _logger.LogInformation(
            "Scoring cycle: source scoring updated. Sources={SourceCount}, Recommended={RecommendedCount}.",
            sourceScoring.sourcesUpdated,
            sourceScoring.recommendedUpdated);

        var rescoredEvents = await eventConfidenceService.RecomputeRecentConfidenceAsync(
            Math.Max(1, options.MaxEvents),
            sinceUtc,
            ct);
        _logger.LogInformation("Scoring cycle: event confidence updated for {Count} events.", rescoredEvents);
    }

    public async Task RunSummarizationCycleAsync(SummarizationOptions options, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var summarizationService = scope.ServiceProvider.GetRequiredService<EventSummarizationService>();

        var maxIterations = Math.Max(1, options.MaxIterations);
        var batchSize = Math.Max(1, options.BatchSize);

        for (var i = 1; i <= maxIterations; i++)
        {
            var summarized = await summarizationService.SummarizePendingEventsAsync(batchSize, ct);
            _logger.LogInformation("Summarization cycle iteration {Iteration}: summarized {Count}.", i, summarized);

            if (summarized == 0)
            {
                break;
            }
        }
    }

    public async Task RunGraphBackfillCycleAsync(BackfillOptions options, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var backfillService = scope.ServiceProvider.GetRequiredService<GraphBackfillService>();

        var result = await backfillService.BackfillRecentAsync(
            Math.Max(1, options.LookbackDays),
            Math.Max(1, options.BatchSize),
            ct);

        _logger.LogInformation(
            "Graph backfill cycle: scanned={PostsScanned}, linksAdded={LinksAdded}, candidatesAdded={CandidatesAdded}, eventsTouched={EventsTouched}.",
            result.PostsScanned,
            result.LinksAdded,
            result.CandidatesAdded,
            result.EventsTouched);
    }

    public async Task RunCandidatePromotionCycleAsync(CandidatePromotionOptions options, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var promotionService = scope.ServiceProvider.GetRequiredService<ExtractedEventCandidatePromotionService>();

        var result = await promotionService.PromotePendingAsync(
            batchSize: Math.Max(1, options.BatchSize),
            matchingThreshold: options.MatchingThreshold,
            maxLinksPerCandidate: Math.Max(1, options.MaxLinksPerCandidate),
            lookbackDays: Math.Max(1, options.LookbackDays),
            maxEventCandidates: Math.Max(25, options.MaxEventCandidates),
            minCreateConfidence: options.MinCreateConfidence,
            ct);

        _logger.LogInformation(
            "Candidate promotion cycle: scanned={CandidatesScanned}, linked={CandidatesLinked}, eventsCreated={EventsCreated}, linksAdded={LinksAdded}, noMatch={NoMatch}, skipped={Skipped}.",
            result.CandidatesScanned,
            result.CandidatesLinked,
            result.EventsCreated,
            result.LinksAdded,
            result.NoMatch,
            result.Skipped);
    }

    public async Task RunProvisionalGcCycleAsync(ProvisionalGcOptions options, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var gcService = scope.ServiceProvider.GetRequiredService<ProvisionalEventGarbageCollectionService>();

        var result = await gcService.MergeDuplicatesAsync(options, ct);
        _logger.LogInformation(
            "Provisional GC cycle: scanned={ScannedEvents}, groups={MergeGroups}, mergedEvents={MergedEvents}, linksMoved={LinksMoved}, linksDeduped={LinksDeduped}, evidenceMoved={EvidenceMoved}, evidenceDeduped={EvidenceDeduped}, summariesResetOrRequeued={SummariesReset}.",
            result.ScannedEvents,
            result.MergeGroups,
            result.MergedEvents,
            result.LinksMoved,
            result.LinksDeduped,
            result.EvidenceMoved,
            result.EvidenceDeduped,
            result.SummariesReset);
    }
}
