using System.Data;
using System.Text.Json;
using Conductor.Shared.Types;

namespace Conductor.Shared;

public static class Converter
{
    public static Result<T> TryDeserializeJson<T>(string data)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(data)!;
        }
        catch (Exception ex)
        {
            return new Error(ex.Message);
        }
    }

    public static Result<T> TryDeserializeJson<T>(Stream data)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(data)!;
        }
        catch (Exception ex)
        {
            return new Error(ex.Message);
        }
    }

    public static DataTable MergeDataTables(List<DataTable> tables)
    {
        if (tables == null || tables.Count == 0)
            throw new ArgumentException("No tables to merge.");

        DataTable mergedTable = tables[0].Clone();

        try
        {
            foreach (var table in tables)
            {
                foreach (DataRow row in table.Rows)
                {
                    mergedTable.ImportRow(row);
                }
            }
        }
        finally
        {
            foreach (var table in tables)
            {
                table.Dispose();
            }
        }

        return mergedTable;
    }
}