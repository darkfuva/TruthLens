using Microsoft.Extensions.Options;

namespace TruthLens.Worker;

public sealed class SummarizationWorker : PeriodicJobBackgroundService
{
    private readonly WorkerPipelineRunner _runner;

    public SummarizationWorker(
        WorkerPipelineRunner runner,
        IOptionsMonitor<WorkerJobsOptions> optionsMonitor,
        ILogger<SummarizationWorker> logger)
        : base(optionsMonitor, logger, nameof(SummarizationWorker))
    {
        _runner = runner;
    }

    protected override JobOptionsBase GetJobOptions(WorkerJobsOptions options) => options.Summarization;

    protected override Task RunJobOnceAsync(WorkerJobsOptions options, CancellationToken ct)
    {
        return _runner.RunSummarizationCycleAsync(options.Summarization, ct);
    }
}
