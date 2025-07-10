using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime;
using Conductor.Shared;
using Conductor.Types;
using Serilog;
using ILogger = Serilog.ILogger;

namespace Conductor.Service;

public class DataTableMemoryManager : IDataTableMemoryManager
{
    private readonly ConcurrentDictionary<string, TrackedDataTable> trackedTables = new();
    private readonly Timer cleanupTimer;
    private readonly Timer gcTimer;
    private readonly ILogger logger = Log.ForContext<DataTableMemoryManager>();
    private readonly Meter meter;
    private volatile bool disposed;

    private readonly Counter<int> tablesCreatedCounter;
    private readonly Counter<int> tablesDisposedCounter;
    private readonly Gauge<int> activeTablesGauge;
    private readonly Gauge<long> memoryUsageGauge;
    private readonly Counter<int> gcTriggeredCounter;

    public DataTableMemoryManager()
    {
        meter = new Meter("Conductor.DataTableMemory", "1.0.0");

        tablesCreatedCounter = meter.CreateCounter<int>(
            "conductor_datatables_created_total",
            description: "Total number of DataTables created");

        tablesDisposedCounter = meter.CreateCounter<int>(
            "conductor_datatables_disposed_total",
            description: "Total number of DataTables disposed");

        activeTablesGauge = meter.CreateGauge<int>(
            "conductor_datatables_active",
            description: "Current number of active DataTables");

        memoryUsageGauge = meter.CreateGauge<long>(
            "conductor_datatable_memory_bytes",
            description: "Estimated memory usage by DataTables in bytes");

        gcTriggeredCounter = meter.CreateCounter<int>(
            "conductor_gc_triggered_total",
            description: "Total number of garbage collections triggered");

        cleanupTimer = new Timer(async _ => await CleanupExpiredTablesAsync(),
            null, TimeSpan.FromMinutes(Settings.DataTableCleanupIntervalMinutes),
            TimeSpan.FromMinutes(Settings.DataTableCleanupIntervalMinutes));

        gcTimer = new Timer(_ => CheckMemoryPressure(),
            null, TimeSpan.FromMinutes(Settings.GcCheckIntervalMinutes),
            TimeSpan.FromMinutes(Settings.GcCheckIntervalMinutes));
    }

    public ManagedDataTable CreateManagedDataTable(string identifier)
    {
        if (disposed) throw new ObjectDisposedException(nameof(DataTableMemoryManager));

        var dataTable = new DataTable();
        var managedTable = new ManagedDataTable(dataTable, identifier, this);

        TrackDataTable(dataTable, identifier);

        tablesCreatedCounter.Add(1, new KeyValuePair<string, object?>("identifier", identifier));
        logger.Debug("Created managed DataTable: {Identifier}", identifier);

        return managedTable;
    }

    public void TrackDataTable(DataTable dataTable, string identifier)
    {
        if (disposed || dataTable is null) return;

        var tracked = new TrackedDataTable(dataTable, identifier, DateTime.UtcNow);
        trackedTables.AddOrUpdate(identifier, tracked, (_, existing) =>
        {
            existing.Dispose();
            return tracked;
        });

        UpdateMetrics();

        logger.Debug("Started tracking DataTable: {Identifier} with {RowCount} rows",
            identifier, dataTable.Rows.Count);
    }

    public void ReleaseDataTable(string identifier)
    {
        if (disposed) return;

        if (trackedTables.TryRemove(identifier, out var tracked))
        {
            tracked.Dispose();
            tablesDisposedCounter.Add(1, new KeyValuePair<string, object?>("identifier", identifier));
            logger.Debug("Released DataTable: {Identifier}", identifier);

            UpdateMetrics();
        }
    }

    public async Task CleanupExpiredTablesAsync()
    {
        if (disposed) return;

        var expiredTables = new List<string>();
        var cutoffTime = DateTime.UtcNow - TimeSpan.FromMinutes(Settings.DataTableLifetimeMinutes);

        foreach (var kvp in trackedTables)
        {
            if (kvp.Value.CreatedAt < cutoffTime)
            {
                expiredTables.Add(kvp.Key);
            }
        }

        if (expiredTables.Count > 0)
        {
            logger.Information("Cleaning up {Count} expired DataTables", expiredTables.Count);

            var cleanupTasks = expiredTables.Select(async id =>
            {
                await Task.Run(() => ReleaseDataTable(id));
            });

            await Task.WhenAll(cleanupTasks);
        }
    }

    public void ForceGarbageCollection()
    {
        logger.Information("Forcing garbage collection due to memory pressure");

        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, true, true);

        gcTriggeredCounter.Add(1);
    }

    public DataTableMemoryStats GetMemoryStats()
    {
        var totalTables = trackedTables.Count;
        var totalEstimatedMemory = 0L;
        var tableDetails = new List<TableMemoryDetail>();

        foreach (var kvp in trackedTables)
        {
            var detail = kvp.Value.GetMemoryDetail();
            totalEstimatedMemory += detail.EstimatedMemoryBytes;
            tableDetails.Add(detail);
        }

        activeTablesGauge.Record(totalTables);
        memoryUsageGauge.Record(totalEstimatedMemory);

        return new DataTableMemoryStats
        {
            TotalActiveTables = totalTables,
            TotalEstimatedMemoryBytes = totalEstimatedMemory,
            TableDetails = tableDetails,
            SystemMemoryInfo = GetSystemMemoryInfo()
        };
    }

    private void CheckMemoryPressure()
    {
        if (disposed) return;

        try
        {
            var stats = GetMemoryStats();
            var memoryPressureThreshold = (long)(Settings.MemoryPressureThresholdGB * 1024 * 1024 * 1024);

            if (stats.TotalEstimatedMemoryBytes > memoryPressureThreshold)
            {
                logger.Warning("Memory pressure detected: {MemoryMB}MB in DataTables",
                    stats.TotalEstimatedMemoryBytes / (1024 * 1024));

                _ = Task.Run(CleanupExpiredTablesAsync);

                if (stats.TotalEstimatedMemoryBytes > memoryPressureThreshold * Settings.MemoryPressureForceGcMultiplier)
                {
                    ForceGarbageCollection();
                }
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error during memory pressure check");
        }
    }

    private void UpdateMetrics()
    {
        var totalTables = trackedTables.Count;
        var totalMemory = trackedTables.Values.Sum(t => t.EstimateMemoryUsage());

        activeTablesGauge.Record(totalTables);
        memoryUsageGauge.Record(totalMemory);
    }

    private static SystemMemoryInfo GetSystemMemoryInfo()
    {
        var process = Process.GetCurrentProcess();

        return new SystemMemoryInfo
        {
            ProcessWorkingSetBytes = process.WorkingSet64,
            ProcessPrivateMemoryBytes = process.PrivateMemorySize64,
            GcTotalMemoryBytes = GC.GetTotalMemory(false),
            Gen0Collections = GC.CollectionCount(0),
            Gen1Collections = GC.CollectionCount(1),
            Gen2Collections = GC.CollectionCount(2)
        };
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        cleanupTimer?.Dispose();
        gcTimer?.Dispose();

        var disposeTasks = trackedTables.Values.Select(t => Task.Run(() => t.Dispose()));
        Task.WaitAll([.. disposeTasks], TimeSpan.FromSeconds(Settings.DataTableDisposeTimeoutSeconds));

        trackedTables.Clear();
        meter?.Dispose();

        logger.Information("DataTable memory manager disposed");
        GC.SuppressFinalize(this);
    }
}