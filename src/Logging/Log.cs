using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Conductor.Shared.Config;
using Conductor.Model;
using Conductor.Service;
using static Conductor.Shared.Constants;
using SQLitePCL;
namespace Conductor.Logging;

public static class Log
{
    private static readonly ConcurrentQueue<Record> logs = new();

    private static readonly string hostname = Environment.MachineName;

    private static string LogPrefix(DateTime time) => $"{GREEN}[{time:yyyy-MM-dd HH:mm:ss:fff}]{NORMAL}::";

    public static void Out(
        string message,
        string? logType = null,
        bool dump = true,
        string? raw = null,
        [CallerMemberName] string? callerMethod = null
    )
    {
        DateTime executionTime = DateTime.Now;
        string logMessage = LogPrefix(executionTime) + $"{BOLD}[{callerMethod}]{NOBOLD}::{MessageWithColor(logType)} > {message}";

        Console.WriteLine(logMessage);

        if (dump && Settings.Logging)
        {
            Record record = new()
            {
                HostName = hostname,
                TimeStamp = executionTime,
                EventType = logType ?? MessageInfo,
                CallerMethod = callerMethod ?? "",
                Event = raw ?? message
            };
            logs.Enqueue(record);

            if (logs.Count > Settings.MaxLogQueueSize)
            {
                TrimLogs();
            }
        }
    }

    public static async Task DumpLogsToFile(RecordService service)
    {
        if (logs.IsEmpty) return;

        Out("Log dump routine started.", dump: false);

        try
        {
            var recordsToDump = new List<Record>();

            while (logs.TryDequeue(out var record))
            {
                recordsToDump.Add(record);
            }

            await service.CreateBulk(recordsToDump);
        }
        catch (Exception ex)
        {
            Out($"Error during log dump: {ex.Message}", MessageError, dump: false);
        }
    }

    private static void TrimLogs()
    {
        while (logs.Count > Settings.MaxLogQueueSize)
        {
            logs.TryDequeue(out _);
        }
    }
}