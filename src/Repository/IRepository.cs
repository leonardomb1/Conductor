using Conductor.Model;
using Conductor.Types;

namespace Conductor.Repository;

public interface IRepository<TModel> where TModel : IDbModel
{
    Task<Result<List<TModel>>> Search(IQueryCollection? filters = null);

    Task<Result<TModel?>> Search(UInt32 id);

    Task<Result<UInt32>> Create(TModel ctx);

    Task<Result> Update(TModel ctx, UInt32 id);

    Task<Result> Delete(UInt32 id);
}