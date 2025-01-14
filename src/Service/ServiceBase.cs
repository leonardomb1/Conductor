using System.Runtime.CompilerServices;
using Conductor.Data;
using Conductor.Logging;
using Conductor.Shared.Config;
using Conductor.Shared.Types;

namespace Conductor.Service;

public abstract class ServiceBase(LdbContext repository) : IDisposable
{
    protected readonly LdbContext Repository = repository;

    protected bool disposed = false;

    protected Error ErrorHandler(Exception ex, [CallerMemberName] string? callingMethod = null)
    {
        Log.Out($"An error has occurred at {callingMethod}, exception message: {ex.Message}, exception stack: {ex.StackTrace}");
        if (Settings.DebugDetailedError)
        {
            return new(ex.Message, ex.StackTrace, method: callingMethod);
        }
        else
        {
            Error error = new(ex.Message)
            {
                FaultedMethod = null
            };

            return error;
        }
    }

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
