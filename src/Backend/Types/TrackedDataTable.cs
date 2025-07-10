using System.Data;
using Conductor.Shared;

namespace Conductor.Types;

public class TrackedDataTable : IDisposable
{
    private readonly DataTable dataTable;
    private volatile bool disposed;

    public string Identifier { get; }
    public DateTime CreatedAt { get; }
    public DateTime LastAccessed { get; private set; }

    public TrackedDataTable(DataTable dataTable, string identifier, DateTime createdAt)
    {
        this.dataTable = dataTable ?? throw new ArgumentNullException(nameof(dataTable));
        Identifier = identifier;
        CreatedAt = createdAt;
        LastAccessed = createdAt;
    }

    public long EstimateMemoryUsage()
    {
        if (disposed || dataTable == null) return 0;

        try
        {
            LastAccessed = DateTime.UtcNow;

            long estimatedBytes = 0;

            foreach (DataColumn column in dataTable.Columns)
            {
                var typeSize = GetTypeSize(column.DataType);
                estimatedBytes += dataTable.Rows.Count * typeSize;
            }

            estimatedBytes += dataTable.Columns.Count * Settings.DataTableColumnOverheadBytes;
            estimatedBytes += dataTable.Rows.Count * Settings.DataTableRowOverheadBytes;

            return estimatedBytes;
        }
        catch
        {
            return 0;
        }
    }

    public TableMemoryDetail GetMemoryDetail()
    {
        return new TableMemoryDetail
        {
            Identifier = Identifier,
            CreatedAt = CreatedAt,
            LastAccessed = LastAccessed,
            RowCount = disposed ? 0 : dataTable?.Rows.Count ?? 0,
            ColumnCount = disposed ? 0 : dataTable?.Columns.Count ?? 0,
            EstimatedMemoryBytes = EstimateMemoryUsage(),
            IsDisposed = disposed
        };
    }

    private static long GetTypeSize(Type type)
    {
        return type switch
        {
            _ when type == typeof(string) => Settings.DefaultStringEstimateBytes,
            _ when type == typeof(int) => sizeof(int),
            _ when type == typeof(long) => sizeof(long),
            _ when type == typeof(double) => sizeof(double),
            _ when type == typeof(decimal) => sizeof(decimal),
            _ when type == typeof(DateTime) => sizeof(long),
            _ when type == typeof(bool) => sizeof(bool),
            _ when type == typeof(byte[]) => Settings.DefaultByteArrayEstimateBytes,
            _ => Settings.DefaultTypeEstimateBytes
        };
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        try
        {
            dataTable?.Clear();
            dataTable?.Dispose();
        }
        catch { }

        GC.SuppressFinalize(this);
    }
}