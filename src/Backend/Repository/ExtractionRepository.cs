
using Conductor.Model;
using Conductor.Shared;
using Conductor.Types;
using Microsoft.EntityFrameworkCore;

namespace Conductor.Repository;

public sealed class ExtractionRepository(EfContext context) : IRepository<Extraction>
{
    public async Task<Result<List<Extraction>>> Search(IQueryCollection? filters)
    {
        try
        {
            var select = context.Extractions
                .Include(e => e.Schedule)
                .Include(e => e.Origin)
                .Include(e => e.Destination)
                .OrderByDescending(e => e.Id)
                .AsQueryable();

            if (filters is not null)
            {
                foreach (var filter in filters)
                {
                    string key = filter.Key.ToString();
                    string value = filter.Value.ToString();
                    string[] arrayVal = filter.Value.ToString().Split(Settings.SplitterChar);

                    select = key switch
                    {
                        "name" => select.Where(e => e.Name == value),
                        "contains" => select.Where(e => arrayVal.Any(s => e.Name.Contains(s))),
                        "schedule" => select.Where(e => e.Schedule != null && e.Schedule.Name == value),
                        "scheduleId" when uint.TryParse(value, out uint schId) =>
                            select.Where(e => e.ScheduleId == schId),
                        "origin" => select.Where(e => e.Origin != null && e.Origin.Name == value),
                        "destination" => select.Where(e => e.Destination != null && e.Destination.Name == value),
                        "take" when uint.TryParse(value, out uint count) => select.Take((int)count),
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

    public async Task<Result<List<SimpleExtractionDto>>> GetNames(List<uint>? ids = null)
    {
        try
        {
            var query = context.Extractions
                .Select(e => new { e.Id, e.Name });

            if (ids is not null && ids.Count > 0)
            {
                query = query.Where(e => ids.Contains(e.Id));
            }

            var results = await query.ToListAsync();

            var dtos = results.Select(e => new SimpleExtractionDto(e.Id, e.Name)).ToList();

            return dtos;
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    public async Task<Result<List<Extraction>>> Search(string[]? filters)
    {
        try
        {
            var select = context.Extractions
                .Include(e => e.Schedule)
                .Include(e => e.Origin)
                .Include(e => e.Destination)
                .AsQueryable();

            if (filters is not null)
            {
                select = select.Where(e => filters.Contains(e.Name));
            }

            return await select.ToListAsync();
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    public static async Task<Result<List<Extraction>>> GetDependencies(Extraction extraction)
    {
        string[] dependencies = extraction.Dependencies?.Split(Settings.SplitterChar) ?? [];

        await using var context = new EfContext();
        var service = new ExtractionRepository(context);

        var dependenciesList = await service.Search(dependencies);
        if (!dependenciesList.IsSuccessful) return dependenciesList.Error;

        Helper.DecryptConnectionStrings(dependenciesList.Value);

        return dependenciesList.Value;
    }

    public async Task<Result<Extraction?>> Search(uint id)
    {
        try
        {
            var extraction = await context.Extractions
                .Include(e => e.Schedule)
                .Include(e => e.Origin)
                .Include(e => e.Destination)
                .FirstOrDefaultAsync(e => e.Id == id);
            return extraction;
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    public async Task<Result<uint>> Create(Extraction extraction)
    {
        try
        {
            await context.Extractions.AddAsync(extraction);
            await context.SaveChangesAsync();
            return extraction.Id;
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    public async Task<Result> Update(Extraction extraction, uint id)
    {
        try
        {
            extraction.Id = id;

            var existingExtraction = await context.Extractions.FindAsync(id);
            if (existingExtraction is null)
                return new Error($"Extraction with id: {id} was not found", null);

            context.Entry(existingExtraction).CurrentValues.SetValues(existingExtraction);
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
            var extraction = await context.Extractions.FindAsync(id);
            if (extraction is null)
                return new Error($"Extraction with id: {id} was not found", null);

            context.Extractions.Remove(extraction);
            await context.SaveChangesAsync();

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }
}