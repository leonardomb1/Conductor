using Conductor.Model;
using Conductor.Types;
using Microsoft.EntityFrameworkCore;

namespace Conductor.Repository;

public sealed class ScheduleRepository(EfContext context) : IRepository<Schedule>
{
    public async Task<Result<List<Schedule>>> Search(IQueryCollection? filters = null)
    {
        try
        {
            var select = from s in context.Schedules
                         select s;

            if (filters is not null)
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
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    public async Task<Result<Schedule?>> Search(uint id)
    {
        try
        {
            var select = from s in context.Schedules
                         where s.Id == id
                         select s;

            return await select.FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    public async Task<Result<uint>> Create(Schedule schedule)
    {
        try
        {
            await context.Schedules.AddAsync(schedule);
            await context.SaveChangesAsync();
            return schedule.Id;
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    public async Task<Result> Update(Schedule schedule, uint id)
    {
        try
        {
            schedule.Id = id;

            var existingSchedule = await context.Schedules.FindAsync(id);
            if (existingSchedule is null)
                return new Error($"Schedule with id: {id} was not found", null);

            context.Entry(existingSchedule).CurrentValues.SetValues(existingSchedule);
            await context.SaveChangesAsync();

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    public async Task<Result> Delete(uint id)
    {
        try
        {
            var schedule = await context.Schedules.FindAsync(id);
            if (schedule is null)
                return new Error($"Schedule with id: {id} was not found", null);

            context.Schedules.Remove(schedule);
            await context.SaveChangesAsync();

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }
}