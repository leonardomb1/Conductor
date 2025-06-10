using Conductor.Repository;
using Conductor.Shared;
using Serilog;

namespace Conductor.Logging;

public class JobRoutine(JobRepository jobRepository, JobExtractionRepository jobExtractionRepository) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await JobTracker.DumpJobs(jobRepository, jobExtractionRepository);
                await Task.Delay(TimeSpan.FromSeconds(Settings.JobRoutineDumpTime), stoppingToken);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.Error($"Error while executing job routine: {ex.InnerException}");
            }
        }
    }
}
