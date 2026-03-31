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
        var lastPendingClustering = int.MaxValue;

        for (var iteration = 1; iteration <= maxIterations; iteration++)
        {
            ct.ThrowIfCancellationRequested();

            var embeddedCount = await embeddingService.GenerateForPendingPostsAsync(
                Math.Max(1, options.EmbeddingBatchSize),
                ct);
            var clusteredCount = await clusteringService.ClusterPendingPostsAsync(
                Math.Max(1, options.ClusteringBatchSize),
                options.ClusteringThreshold,
                ct);

            var pendingEmbedding = await db.Posts.CountAsync(p => p.Embedding == null, ct);
            var pendingClustering = await db.Posts.CountAsync(p => p.Embedding != null && p.EventId == null, ct);

            _logger.LogInformation(
                "Ingestion cycle iteration {Iteration}: embedded={EmbeddedCount}, clustered={ClusteredCount}, pendingEmbedding={PendingEmbedding}, pendingClustering={PendingClustering}.",
                iteration,
                embeddedCount,
                clusteredCount,
                pendingEmbedding,
                pendingClustering);

            if (pendingEmbedding == 0 && pendingClustering == 0)
            {
                break;
            }

            if (pendingEmbedding == lastPendingEmbedding && pendingClustering == lastPendingClustering)
            {
                _logger.LogWarning("Ingestion cycle stopped due to no progress at iteration {Iteration}.", iteration);
                break;
            }

            lastPendingEmbedding = pendingEmbedding;
            lastPendingClustering = pendingClustering;
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
        var lastPendingClustering = int.MaxValue;
        var maxCatchupIterations = Math.Max(1, options.PostPromotionMaxCatchupIterations);

        for (var i = 1; i <= maxCatchupIterations; i++)
        {
            var embedded = await embeddingService.GenerateForPendingPostsAsync(Math.Max(1, ingestionOptions.EmbeddingBatchSize), ct);
            var clustered = await clusteringService.ClusterPendingPostsAsync(
                Math.Max(1, ingestionOptions.ClusteringBatchSize),
                ingestionOptions.ClusteringThreshold,
                ct);

            var pendingEmbedding = await db.Posts.CountAsync(p => p.Embedding == null, ct);
            var pendingClustering = await db.Posts.CountAsync(p => p.Embedding != null && p.EventId == null, ct);

            _logger.LogInformation(
                "Discovery post-promotion iteration {Iteration}: embedded={Embedded}, clustered={Clustered}, pendingEmbedding={PendingEmbedding}, pendingClustering={PendingClustering}.",
                i,
                embedded,
                clustered,
                pendingEmbedding,
                pendingClustering);

            if (pendingEmbedding == 0 && pendingClustering == 0)
            {
                break;
            }

            if (pendingEmbedding == lastPendingEmbedding && pendingClustering == lastPendingClustering)
            {
                _logger.LogWarning("Discovery post-promotion catch-up stopped due to no progress at iteration {Iteration}.", i);
                break;
            }

            lastPendingEmbedding = pendingEmbedding;
            lastPendingClustering = pendingClustering;
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
}
