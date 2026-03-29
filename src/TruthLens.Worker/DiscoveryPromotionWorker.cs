using Microsoft.Extensions.Options;

namespace TruthLens.Worker;

public sealed class DiscoveryPromotionWorker : PeriodicJobBackgroundService
{
    private readonly WorkerPipelineRunner _runner;

    public DiscoveryPromotionWorker(
        WorkerPipelineRunner runner,
        IOptionsMonitor<WorkerJobsOptions> optionsMonitor,
        ILogger<DiscoveryPromotionWorker> logger)
        : base(optionsMonitor, logger, nameof(DiscoveryPromotionWorker))
    {
        _runner = runner;
    }

    protected override JobOptionsBase GetJobOptions(WorkerJobsOptions options) => options.Discovery;

    protected override Task RunJobOnceAsync(WorkerJobsOptions options, CancellationToken ct)
    {
        return _runner.RunDiscoveryPromotionCycleAsync(options.Discovery, options.Ingestion, ct);
    }
}
