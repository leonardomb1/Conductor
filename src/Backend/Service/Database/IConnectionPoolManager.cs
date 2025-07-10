using System.Data.Common;
using Conductor.Types;

namespace Conductor.Service.Database;

public interface IConnectionPoolManager : IAsyncDisposable, IDisposable
{
    DbConnection GetConnection(string connectionString, string dbType);
    Task<DbConnection> GetConnectionAsync(string connectionString, string dbType, CancellationToken cancellationToken = default);
    void ReturnConnection(string connectionKey, DbConnection connection);
    ConnectionPoolStats GetPoolStats();
    Task CleanupIdleConnectionsAsync();
}