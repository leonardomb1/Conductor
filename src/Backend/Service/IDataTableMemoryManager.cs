using System.Data;
using Conductor.Types;

namespace Conductor.Service;

public interface IDataTableMemoryManager : IDisposable
{
    ManagedDataTable CreateManagedDataTable(string identifier);
    void TrackDataTable(DataTable dataTable, string identifier);
    void ReleaseDataTable(string identifier);
    void ForceGarbageCollection();
    DataTableMemoryStats GetMemoryStats();
    Task CleanupExpiredTablesAsync();
}