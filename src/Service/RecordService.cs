using Conductor.Data;
using Conductor.Model;
using Conductor.Shared.Types;
using LinqToDB;
using LinqToDB.Data;

namespace Conductor.Service;

public class RecordService(LdbContext context) : ServiceBase(context), IService<Record>
{
    public async Task<Result<List<Record>>> Search(IQueryCollection? filters = null)
    {
        try
        {
            var select = (from r in Repository.Records
                          orderby r.TimeStamp descending
                          select r).AsQueryable();

            if (filters != null)
            {
                foreach (var filter in filters)
                {
                    string key = filter.Key.ToString();
                    string value = filter.Value.ToString();

                    select = key switch
                    {
                        "relative" when Int32.TryParse(value, out var time) => select.Where(
                                e => e.TimeStamp >= DateTime.Now.AddSeconds(-time)
                            ),
                        "hostname" => select.Where(e => e.HostName == value),
                        "type" => select.Where(e => e.EventType == value),
                        "event" => select.Where(e => e.Event.Contains(value)),
                        "take" when Int32.TryParse(value, out Int32 count) => select.Take(count),
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

    public async Task<Result<Record?>> Search(UInt32 id)
    {
        try
        {
            var select = from r in Repository.Records
                         where r.Id == id
                         select r;

            return await select.FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            return ErrorHandler(ex);
        }
    }

    public async Task<Result<Int32>> Count()
    {
        try
        {
            var select = from s in Repository.Records
                         select s;

            return await select.CountAsync();
        }
        catch (Exception ex)
        {
            return ErrorHandler(ex);
        }
    }

    public async Task<Result> CreateBulk(List<Record> record)
    {
        try
        {
            var insert = await Repository.BulkCopyAsync(record);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return ErrorHandler(ex);
        }
    }

    public async Task<Result> Clear()
    {
        try
        {
            await Repository.Origins
                .TruncateAsync();

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return ErrorHandler(ex);
        }
    }

    public Task<Result> Delete(UInt32 id)
    {
        throw new NotImplementedException();
    }

    public Task<Result> Create(Record record)
    {
        throw new NotImplementedException();
    }

    public Task<Result> Update(Record record, UInt32 id)
    {
        throw new NotImplementedException();
    }
}