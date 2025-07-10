using System.Data;
using System.Data.Common;

namespace Conductor.Service.Database;

public sealed class PooledConnection(DbConnection connection, DateTimeOffset lastUse) : IAsyncDisposable, IDisposable
{
    private readonly DbConnection connection = connection ?? throw new ArgumentNullException(nameof(connection));
    private volatile bool disposed;
    public DateTimeOffset LastUsed { get; set; } = lastUse;
    public DbConnection Connection => disposed ? throw new ObjectDisposedException(nameof(PooledConnection)) : connection;

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;

        try
        {
            if (connection.State != ConnectionState.Closed)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
            await connection.DisposeAsync().ConfigureAwait(false);
        }
        catch { }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        try
        {
            if (connection.State != ConnectionState.Closed)
            {
                connection.Close();
            }
            connection.Dispose();
        }
        catch { }
    }
}