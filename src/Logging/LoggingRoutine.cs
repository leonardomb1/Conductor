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
                await Log.DumpLogsToFile(recordService);
                await Task.Delay(TimeSpan.FromSeconds(Settings.LogDumpTime), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                Log.Out($"Stopping...", Constants.MessageInfo, dump: false, callerMethod: "Server");
            }
            catch (Exception ex)
            {
                Log.Out($"Error while executing logging routine: {ex.InnerException}", Constants.MessageError, dump: true, callerMethod: "LoggingRoutine");
            }
        }
    }
}
