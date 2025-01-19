using Conductor.Data;
using Conductor.Model;
using Conductor.Shared.Types;
using LinqToDB;

namespace Conductor.Service;

public sealed class ScheduleService(LdbContext context) : ServiceBase(context), IService<Schedule>
{
    public async Task<Result<List<Schedule>>> Search(IQueryCollection? filters = null)
    {
        try
        {
            var select = from s in Repository.Schedules
                         select s;

            if (filters != null)
            {
                foreach (var filter in filters)
                {
                    string key = filter.Key.ToString();
                    string value = filter.Value.ToString();

                    select = key switch
                    {
                        "name" => select.Where(e => e.Name == value),
                        "status" when bool.TryParse(filter.Value, out var sts) => select.Where(e => e.Status == sts),
                        _ => select
                    };
                }
            }

            return await select.ToListAsync();
        }
        catch (Exception ex)
        {
            return ErrorHandler(ex);
        }
    }

    public async Task<Result<Schedule?>> Search(UInt32 id)
    {
        try
        {
            var select = from s in Repository.Schedules
                         where s.Id == id
                         select s;

            return await select.FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            return ErrorHandler(ex);
        }
    }

    public async Task<Result> Create(Schedule schedule)
    {
        try
        {
            var insert = await Repository.InsertAsync(schedule);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return ErrorHandler(ex);
        }
    }

    public async Task<Result> Update(Schedule schedule, UInt32 id)
    {
        try
        {
            schedule.Id = id;

            await Repository.UpdateAsync(schedule);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return ErrorHandler(ex);
        }
    }

    public async Task<Result> Delete(UInt32 id)
    {
        try
        {
            await Repository.Schedules
                .Where(s => s.Id == id)
                .DeleteAsync();

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return ErrorHandler(ex);
        }
    }
}