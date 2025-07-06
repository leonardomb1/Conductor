using System.Data;

namespace Conductor.Service.Script;

public interface IScript
{
    Task<DataTable> ExecuteAsync<T>(T context, CancellationToken token = default) where T : class;
}