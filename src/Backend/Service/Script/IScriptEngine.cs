using System.Data;
using Conductor.Types;

namespace Conductor.Service.Script;

public interface IScriptEngine
{
    Task<Result<DataTable>> ExecuteAsync<T>(string script, T context, CancellationToken token = default) where T : class;
    Task<Result<IScript>> CompileAsync(string script, CancellationToken token = default);
}