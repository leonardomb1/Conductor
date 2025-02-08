using System.Data;
using System.Text.Json;
using Conductor.Shared.Config;
using Conductor.Shared.Types;

namespace Conductor.Shared;

public static class Converter
{
    public static async Task<Result<T>> TryDeserializeJson<T>(Stream data)
    {
        try
        {
            var json = await JsonSerializer.DeserializeAsync<T>(
                data,
                Settings.JsonSOptions.Value
            );
            return json!;
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    public static async Task<Result> TrySerializeAsync<T>(Stream stream, T data)
    {
        try
        {
            await JsonSerializer.SerializeAsync<T>(
                stream,
                data,
                Settings.JsonSOptions.Value
            );
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
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