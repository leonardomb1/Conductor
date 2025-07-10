namespace Conductor.Types;

public class ManagedDataTable : IDisposable
{
    private readonly DataTable datatable;
    private readonly string identifier;
    private readonly IDataTableMemoryManager manager;
    private volatile bool diposed;

    public DataTable Table => diposed ? throw new ObjectDisposedException(nameof(ManagedDataTable)) : datatable;

    internal ManagedDataTable(DataTable dt, string id, IDataTableMemoryManager mnger)
    {
        datatable = dt ?? throw new ArgumentNullException(nameof(dataTable));
        identifier = id ?? throw new ArgumentNullException(nameof(identifier));
        manager = mnger ?? throw new ArgumentNullException(nameof(manager));
    }

    public void Dispose()
    {
        if (diposed) return;
        diposed = true;

        manager.ReleaseDataTable(identifier);
        GC.SuppressFinalize(this);
    }
}