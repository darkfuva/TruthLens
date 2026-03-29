using Microsoft.Extensions.Options;

namespace TruthLens.Worker;

public abstract class PeriodicJobBackgroundService : BackgroundService
{
    private readonly IOptionsMonitor<WorkerJobsOptions> _optionsMonitor;
    private readonly ILogger _logger;
    private readonly string _jobName;

    protected PeriodicJobBackgroundService(
        IOptionsMonitor<WorkerJobsOptions> optionsMonitor,
        ILogger logger,
        string jobName)
    {
        _optionsMonitor = optionsMonitor;
        _logger = logger;
        _jobName = jobName;
    }

    protected abstract JobOptionsBase GetJobOptions(WorkerJobsOptions options);
    protected abstract Task RunJobOnceAsync(WorkerJobsOptions options, CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _optionsMonitor.CurrentValue;
            var job = GetJobOptions(options);

            try
            {
                if (job.Enabled)
                {
                    await RunJobOnceAsync(options, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{JobName} failed.", _jobName);
            }

            var delaySeconds = Math.Max(5, job.IntervalSeconds);
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
        }
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        var options = _optionsMonitor.CurrentValue;
        var job = GetJobOptions(options);

        // Run once at startup when enabled, then continue periodic loop.
        if (job.Enabled && job.RunOnStart)
        {
            try
            {
                await RunJobOnceAsync(options, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{JobName} startup run failed.", _jobName);
            }
        }

        await base.StartAsync(cancellationToken);
    }
}
