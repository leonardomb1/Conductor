using System.Data;
using Conductor.Service;

namespace Conductor.Types;

public class ManagedDataTable : IDisposable
{
    private readonly DataTable dataTable;
    private readonly string identifier;
    private readonly IDataTableMemoryManager manager;
    private volatile bool disposed;

    public DataTable Table => disposed ? throw new ObjectDisposedException(nameof(ManagedDataTable)) : dataTable;

    internal ManagedDataTable(DataTable dataTable, string identifier, IDataTableMemoryManager manager)
    {
        this.dataTable = dataTable ?? throw new ArgumentNullException(nameof(dataTable));
        this.identifier = identifier ?? throw new ArgumentNullException(nameof(identifier));
        this.manager = manager ?? throw new ArgumentNullException(nameof(manager));
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        manager.ReleaseDataTable(identifier);
        GC.SuppressFinalize(this);
    }
}