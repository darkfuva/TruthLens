using Microsoft.Extensions.Options;

namespace TruthLens.Worker;

public sealed class IngestionEmbeddingClusteringWorker : PeriodicJobBackgroundService
{
    private readonly WorkerPipelineRunner _runner;

    public IngestionEmbeddingClusteringWorker(
        WorkerPipelineRunner runner,
        IOptionsMonitor<WorkerJobsOptions> optionsMonitor,
        ILogger<IngestionEmbeddingClusteringWorker> logger)
        : base(optionsMonitor, logger, nameof(IngestionEmbeddingClusteringWorker))
    {
        _runner = runner;
    }

    protected override JobOptionsBase GetJobOptions(WorkerJobsOptions options) => options.Ingestion;

    protected override Task RunJobOnceAsync(WorkerJobsOptions options, CancellationToken ct)
    {
        return _runner.RunIngestionEmbeddingClusteringCycleAsync(options.Ingestion, ct);
    }
}
