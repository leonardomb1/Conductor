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
                .AsQueryable();

            if (filters is not null)
            {
                foreach (var filter in filters)
                {
                    string key = filter.Key.ToString();
                    string value = filter.Value.ToString();
                    string[] arrayVal = filter.Value.ToString().Split(Settings.SplitterChar);

                    if (key == "ids" && !string.IsNullOrEmpty(value))
                    {
                        var parsedIds = value.Split(',')
                            .Select(id => uint.TryParse(id.Trim(), out uint parsedId) ? parsedId : 0)
                            .Where(id => id > 0)
                            .ToList();

                        select = select.Where(e => parsedIds.Contains(e.Id));
                        continue;
                    }

                    select = key switch
                    {
                        "name" => select.Where(e => e.Name == value),
                        "contains" => select.Where(e => arrayVal.Any(s => e.Name.Contains(s))),
                        "schedule" => select.Where(e => e.Schedule != null && e.Schedule.Name == value),
                        "scheduleId" when uint.TryParse(value, out uint schId) =>
                            select.Where(e => e.ScheduleId == schId),
                        "originId" when uint.TryParse(value, out uint originId) =>
                            select.Where(e => e.OriginId == originId),
                        "destinationId" when uint.TryParse(value, out uint destId) =>
                            select.Where(e => e.DestinationId == destId),
                        "origin" => select.Where(e => e.Origin != null && e.Origin.Name == value),
                        "destination" => select.Where(e => e.Destination != null && e.Destination.Name == value),
                        "sourceType" => select.Where(e => e.SourceType == value),
                        "isIncremental" when bool.TryParse(value, out bool isInc) =>
                            select.Where(e => e.IsIncremental == isInc),
                        "isVirtual" when bool.TryParse(value, out bool isVirt) =>
                            select.Where(e => e.IsVirtual == isVirt),
                        "search" => select.Where(e =>
                            e.Name.Contains(value) ||
                            (e.Alias != null && e.Alias.Contains(value)) ||
                            (e.IndexName != null && e.IndexName.Contains(value))),
                        "skip" => select,
                        "take" => select,
                        "sortBy" => select,
                        "sortDirection" => select,
                        _ => select
                    };
                }
            }

            var sortBy = filters?["sortBy"].FirstOrDefault() ?? "id";
            var sortDirection = filters?["sortDirection"].FirstOrDefault() ?? "desc";

            select = sortBy.ToLowerInvariant() switch
            {
                "name" => sortDirection == "asc" ?
                    select.OrderBy(e => e.Name) :
                    select.OrderByDescending(e => e.Name),
                "sourcetype" => sortDirection == "asc" ?
                    select.OrderBy(e => e.SourceType) :
                    select.OrderByDescending(e => e.SourceType),
                "origin" => sortDirection == "asc" ?
                    select.OrderBy(e => e.Origin != null ? e.Origin.Name : "") :
                    select.OrderByDescending(e => e.Origin != null ? e.Origin.Name : ""),
                "destination" => sortDirection == "asc" ?
                    select.OrderBy(e => e.Destination != null ? e.Destination.Name : "") :
                    select.OrderByDescending(e => e.Destination != null ? e.Destination.Name : ""),
                "schedule" => sortDirection == "asc" ?
                    select.OrderBy(e => e.Schedule != null ? e.Schedule.Name : "") :
                    select.OrderByDescending(e => e.Schedule != null ? e.Schedule.Name : ""),
                "isincremental" => sortDirection == "asc" ?
                    select.OrderBy(e => e.IsIncremental) :
                    select.OrderByDescending(e => e.IsIncremental),
                _ => sortDirection == "asc" ?
                    select.OrderBy(e => e.Id) :
                    select.OrderByDescending(e => e.Id)
            };

            if (filters is not null)
            {
                if (uint.TryParse(filters["skip"], out uint skip))
                {
                    select = select.Skip((int)skip);
                }

                if (uint.TryParse(filters["take"], out uint take))
                {
                    select = select.Take((int)take);
                }
            }

            return await select.ToListAsync();
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }
    public async Task<Result<int>> GetCount(IQueryCollection? filters)
    {
        try
        {
            var select = context.Extractions.AsQueryable();

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
                        "scheduleId" when uint.TryParse(value, out uint schId) =>
                            select.Where(e => e.ScheduleId == schId),
                        "originId" when uint.TryParse(value, out uint originId) =>
                            select.Where(e => e.OriginId == originId),
                        "destinationId" when uint.TryParse(value, out uint destId) =>
                            select.Where(e => e.DestinationId == destId),
                        "sourceType" => select.Where(e => e.SourceType == value),
                        "isIncremental" when bool.TryParse(value, out bool isInc) =>
                            select.Where(e => e.IsIncremental == isInc),
                        "isVirtual" when bool.TryParse(value, out bool isVirt) =>
                            select.Where(e => e.IsVirtual == isVirt),
                        "search" => select.Where(e =>
                            e.Name.Contains(value) ||
                            (e.Alias != null && e.Alias.Contains(value)) ||
                            (e.IndexName != null && e.IndexName.Contains(value))),
                        "skip" or "take" or "sortBy" or "sortDirection" => select,
                        _ => select
                    };
                }
            }

            return await select.CountAsync();
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

            context.Entry(existingExtraction).CurrentValues.SetValues(extraction);
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