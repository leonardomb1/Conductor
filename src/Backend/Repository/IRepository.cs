using Conductor.Model;
using Conductor.Types;

namespace Conductor.Repository;

public interface IRepository<TModel> where TModel : IDbModel
{
    Task<Result<List<TModel>>> Search(IQueryCollection? filters = null);

    Task<Result<TModel?>> Search(uint id);

    Task<Result<uint>> Create(TModel ctx);

    Task<Result> Update(TModel ctx, uint id);

    Task<Result> Delete(uint id);
}