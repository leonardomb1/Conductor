using Conductor.Data;

namespace Conductor.Service;

public abstract class ServiceBase(LdbContext repository) : IDisposable
{
    protected readonly LdbContext Repository = repository;

    protected bool disposed = false;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (!disposed && disposing)
        {
            Repository.Dispose();
        }
    }

    ~ServiceBase()
    {
        Dispose(false);
    }
}
