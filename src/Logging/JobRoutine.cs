using Conductor.Shared;
using Conductor.Shared.Config;
using Conductor.Service;

namespace Conductor.Logging;

public class JobRoutine(JobService jobService, JobExtractionService jobExtractionService) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await JobTracker.DumpJobs(jobService, jobExtractionService);
                await Task.Delay(TimeSpan.FromSeconds(Settings.LogDumpTimeSec), stoppingToken);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.Out($"Error while executing job routine: {ex.InnerException}", RecordType.Error, dump: true, callerMethod: "JobRoutine");
            }
        }
    }
}
