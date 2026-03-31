using Microsoft.Extensions.Options;
using System.Diagnostics;

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
        var firstIteration = true;

        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _optionsMonitor.CurrentValue;
            var job = GetJobOptions(options);

            try
            {
                if (job.Enabled)
                {
                    if (firstIteration && !job.RunOnStart)
                    {
                        _logger.LogInformation("{JobName} startup run skipped (RunOnStart=false).", _jobName);
                    }
                    else
                    {
                        var sw = Stopwatch.StartNew();
                        _logger.LogInformation("{JobName} cycle started.", _jobName);
                        await RunJobOnceAsync(options, stoppingToken);
                        sw.Stop();
                        _logger.LogInformation("{JobName} cycle completed in {ElapsedMs} ms.", _jobName, sw.ElapsedMilliseconds);
                    }
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
            finally
            {
                firstIteration = false;
            }

            var delaySeconds = Math.Max(5, job.IntervalSeconds);
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
        }
    }
}
