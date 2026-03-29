using Microsoft.Extensions.Options;

namespace TruthLens.Worker;

public sealed class ScoringWorker : PeriodicJobBackgroundService
{
    private readonly WorkerPipelineRunner _runner;

    public ScoringWorker(
        WorkerPipelineRunner runner,
        IOptionsMonitor<WorkerJobsOptions> optionsMonitor,
        ILogger<ScoringWorker> logger)
        : base(optionsMonitor, logger, nameof(ScoringWorker))
    {
        _runner = runner;
    }

    protected override JobOptionsBase GetJobOptions(WorkerJobsOptions options) => options.Scoring;

    protected override Task RunJobOnceAsync(WorkerJobsOptions options, CancellationToken ct)
    {
        return _runner.RunScoringCycleAsync(options.Scoring, ct);
    }
}
