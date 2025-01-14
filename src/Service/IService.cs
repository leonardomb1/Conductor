using Conductor.Model;
using Conductor.Shared.Types;

namespace Conductor.Service;

public interface IService<TModel> where TModel : IDbModel
{
    Task<Result<List<TModel>>> Search(IQueryCollection? filters = null);

    Task<Result<TModel?>> Search(UInt32 id);

    Task<Result> Create(TModel ctx);

    Task<Result> Update(TModel ctx, UInt32 id);

    Task<Result> Delete(UInt32 id);
}