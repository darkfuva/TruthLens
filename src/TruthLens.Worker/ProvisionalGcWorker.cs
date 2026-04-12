using Microsoft.Extensions.Options;

namespace TruthLens.Worker;

public sealed class ProvisionalGcWorker : PeriodicJobBackgroundService
{
    private readonly WorkerPipelineRunner _runner;

    public ProvisionalGcWorker(
        WorkerPipelineRunner runner,
        IOptionsMonitor<WorkerJobsOptions> optionsMonitor,
        ILogger<ProvisionalGcWorker> logger)
        : base(optionsMonitor, logger, nameof(ProvisionalGcWorker))
    {
        _runner = runner;
    }

    protected override JobOptionsBase GetJobOptions(WorkerJobsOptions options) => options.ProvisionalGc;

    protected override Task RunJobOnceAsync(WorkerJobsOptions options, CancellationToken ct)
    {
        return _runner.RunProvisionalGcCycleAsync(options.ProvisionalGc, ct);
    }
}

