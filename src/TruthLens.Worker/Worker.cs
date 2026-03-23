using Microsoft.Extensions.DependencyInjection;
using TruthLens.Application.Services.Embedding;
using TruthLens.Application.Services.Rss;

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
        try
        {
            // Worker is singleton; create a scope to resolve scoped services like DbContext/repositories.
            using var scope = _scopeFactory.CreateScope();

            var ingestionService = scope.ServiceProvider.GetRequiredService<RssIngestionService>();
            var insertedCount = await ingestionService.IngestAllAsync(stoppingToken);
            var embeddingService = scope.ServiceProvider.GetRequiredService<EmbeddingGenerationService>();
            var embeddedCount = await embeddingService.GenerateForPendingPostsAsync(64, stoppingToken);

            _logger.LogInformation("Embedding generation completed. Embedded {EmbeddedCount} posts.", embeddedCount);
            _logger.LogInformation("RSS ingestion completed. Inserted {InsertedCount} posts.", insertedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RSS ingestion failed.");
        }
        finally
        {
            // One-shot mode: stop process after one run.
            _hostApplicationLifetime.StopApplication();
        }
    }
}
