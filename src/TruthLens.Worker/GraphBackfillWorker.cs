using Microsoft.Extensions.Options;

namespace TruthLens.Worker;

public sealed class GraphBackfillWorker : PeriodicJobBackgroundService
{
    private readonly WorkerPipelineRunner _runner;

    public GraphBackfillWorker(
        WorkerPipelineRunner runner,
        IOptionsMonitor<WorkerJobsOptions> optionsMonitor,
        ILogger<GraphBackfillWorker> logger)
        : base(optionsMonitor, logger, nameof(GraphBackfillWorker))
    {
        _runner = runner;
    }

    protected override JobOptionsBase GetJobOptions(WorkerJobsOptions options) => options.Backfill;

    protected override Task RunJobOnceAsync(WorkerJobsOptions options, CancellationToken ct)
    {
        return _runner.RunGraphBackfillCycleAsync(options.Backfill, ct);
    }
}
