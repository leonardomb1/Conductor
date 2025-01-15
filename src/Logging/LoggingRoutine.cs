using Conductor.Service;
using Conductor.Shared;
using Conductor.Shared.Config;

namespace Conductor.Logging;

public class LoggingRoutine(RecordService recordService) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Log.DumpLogs(recordService);
                await Task.Delay(TimeSpan.FromSeconds(Settings.LogDumpTimeSec), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                Log.Out($"Stopping...", RecordType.Info, dump: false, callerMethod: "Server");
            }
            catch (Exception ex)
            {
                Log.Out($"Error while executing logging routine: {ex.InnerException}", RecordType.Error, dump: true, callerMethod: "LoggingRoutine");
            }
        }
    }
}
