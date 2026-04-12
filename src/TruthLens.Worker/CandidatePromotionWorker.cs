using Microsoft.Extensions.Options;

namespace TruthLens.Worker;

public sealed class CandidatePromotionWorker : PeriodicJobBackgroundService
{
    private readonly WorkerPipelineRunner _runner;

    public CandidatePromotionWorker(
        WorkerPipelineRunner runner,
        IOptionsMonitor<WorkerJobsOptions> optionsMonitor,
        ILogger<CandidatePromotionWorker> logger)
        : base(optionsMonitor, logger, nameof(CandidatePromotionWorker))
    {
        _runner = runner;
    }

    protected override JobOptionsBase GetJobOptions(WorkerJobsOptions options) => options.CandidatePromotion;

    protected override Task RunJobOnceAsync(WorkerJobsOptions options, CancellationToken ct)
    {
        return _runner.RunCandidatePromotionCycleAsync(options.CandidatePromotion, ct);
    }
}

