using Microsoft.Extensions.DependencyInjection;
using TruthLens.Application.Services.Clustering;
using TruthLens.Application.Services.Embedding;
using TruthLens.Application.Services.Rss;
using TruthLens.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using TruthLens.Application.Services.Summarization;
namespace TruthLens.Worker;

public sealed class Worker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<Worker> _logger;
    private readonly IHostApplicationLifetime _hostApplicationLifetime;

    public Worker(
        IServiceScopeFactory scopeFactory,
        ILogger<Worker> logger,
        IHostApplicationLifetime hostApplicationLifetime)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _hostApplicationLifetime = hostApplicationLifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        const int maxIterations = 20;
        const int embeddingBatchSize = 64;
        const int clusteringBatchSize = 100;
        const double clusteringThreshold = 0.82;

        try
        {
            // 1) Ingest once per run
            using (var scope = _scopeFactory.CreateScope())
            {
                var ingestionService = scope.ServiceProvider.GetRequiredService<RssIngestionService>();
                var insertedCount = await ingestionService.IngestAllAsync(stoppingToken);
                _logger.LogInformation("RSS ingestion completed. Inserted {InsertedCount} posts.", insertedCount);
            }

            var lastPendingEmbedding = int.MaxValue;
            var lastPendingClustering = int.MaxValue;

            // 2) Loop embedding + clustering until no pending work (or safety stop)
            for (var iteration = 1; iteration <= maxIterations; iteration++)
            {
                stoppingToken.ThrowIfCancellationRequested();

                using var scope = _scopeFactory.CreateScope();

                var embeddingService = scope.ServiceProvider.GetRequiredService<EmbeddingGenerationService>();
                var clusteringService = scope.ServiceProvider.GetRequiredService<ClusteringService>();
                var db = scope.ServiceProvider.GetRequiredService<TruthLensDbContext>();

                var embeddedCount = await embeddingService.GenerateForPendingPostsAsync(embeddingBatchSize, stoppingToken);
                var clusteredCount = await clusteringService.ClusterPendingPostsAsync(clusteringBatchSize, clusteringThreshold, stoppingToken);

                var pendingEmbedding = await db.Posts.CountAsync(p => p.Embedding == null, stoppingToken);
                var pendingClustering = await db.Posts.CountAsync(
                    p => p.Embedding != null && p.EventId == null,
                    stoppingToken);

                var avgScore = await db.Posts
                    .Where(p => p.ClusterAssignmentScore != null && p.EventId != null)
                    .Select(p => p.ClusterAssignmentScore)
                    .AverageAsync(stoppingToken);

                _logger.LogInformation(
                    "Iteration {Iteration}: Embedded {EmbeddedCount}, Clustered {ClusteredCount}, PendingEmbedding {PendingEmbedding}, PendingClustering {PendingClustering}, AvgScore {AvgScore}",
                    iteration,
                    embeddedCount,
                    clusteredCount,
                    pendingEmbedding,
                    pendingClustering,
                    avgScore is null ? "N/A" : avgScore.Value.ToString("F4"));


                if (pendingEmbedding == 0 && pendingClustering == 0)
                {
                    _logger.LogInformation("Pipeline catch-up complete.");
                    break;
                }

                if (pendingEmbedding == lastPendingEmbedding && pendingClustering == lastPendingClustering)
                {
                    _logger.LogWarning(
                        "No progress detected at iteration {Iteration}. Stopping to avoid infinite loop.",
                        iteration);
                    break;
                }

                lastPendingEmbedding = pendingEmbedding;
                lastPendingClustering = pendingClustering;
            }

            using (var scope = _scopeFactory.CreateScope())
            {
                const int maxSummaryIterations = 20;
                var summarizationService = scope.ServiceProvider.GetRequiredService<EventSummarizationService>();

                for (var i = 1; i <= maxSummaryIterations; i++)
                {
                    var summarized = await summarizationService.SummarizePendingEventsAsync(10, stoppingToken);
                    _logger.LogInformation("Summary iteration {Iteration}: summarized {Count}", i, summarized);

                    if (summarized == 0) break; // no more pending (or all remaining are skipped)
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Worker pipeline failed.");
        }
        finally
        {
            _hostApplicationLifetime.StopApplication();
        }
    }
}
