using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;
using Conductor.Types;

namespace Conductor.Shared;

public static class Converter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

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
        if (tables is null || tables.Count == 0)
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

    public static Result<DataTable> JsonToDataTable(string json)
    {
        if (string.IsNullOrEmpty(json))
            return new DataTable();

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var dataTable = new DataTable();

        try
        {
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                var firstElement = root[0];
                var columns = new List<string>();

                if (firstElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in firstElement.EnumerateObject())
                    {
                        columns.Add(property.Name);
                        var columnType = GetColumnType(property.Value);
                        dataTable.Columns.Add(property.Name, columnType);
                    }
                }

                foreach (var element in root.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.Object)
                    {
                        var row = dataTable.NewRow();

                        foreach (var property in element.EnumerateObject())
                        {
                            if (columns.Contains(property.Name))
                            {
                                row[property.Name] = GetValue(property.Value);
                            }
                        }

                        dataTable.Rows.Add(row);
                    }
                }
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in root.EnumerateObject())
                {
                    var columnType = GetColumnType(property.Value);
                    dataTable.Columns.Add(property.Name, columnType);
                }

                var row = dataTable.NewRow();
                foreach (var property in root.EnumerateObject())
                {
                    row[property.Name] = GetValue(property.Value);
                }
                dataTable.Rows.Add(row);
            }

            return dataTable;
        }
        catch (Exception ex)
        {
            return new Error($"Failed to convert JSON to DataTable: {ex.Message}", ex.StackTrace);
        }
    }

    public static Result<DataTable> JsonToDataTableStreaming(Stream jsonStream)
    {
        using var document = JsonDocument.Parse(jsonStream);
        return ProcessJsonDocument(document.RootElement);
    }

    public static Result<DataTable> JsonToDataTable(ReadOnlySpan<byte> jsonUtf8)
    {
        var reader = new Utf8JsonReader(jsonUtf8);
        using var document = JsonDocument.ParseValue(ref reader);
        return ProcessJsonDocument(document.RootElement);
    }

    public static Result<DataTable> ProcessJsonDocument(JsonElement root)
    {
        var dataTable = new DataTable();
        try
        {
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                var columns = new Dictionary<string, Type>();
                foreach (var element in root.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var property in element.EnumerateObject())
                        {
                            if (!columns.ContainsKey(property.Name))
                                columns[property.Name] = GetColumnType(property.Value);
                        }
                    }
                }
                foreach (var col in columns)
                {
                    dataTable.Columns.Add(col.Key, col.Value);
                }
                foreach (var element in root.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.Object)
                    {
                        var row = dataTable.NewRow();
                        foreach (DataColumn col in dataTable.Columns)
                        {
                            if (element.TryGetProperty(col.ColumnName, out var property))
                            {
                                row[col.ColumnName] = GetValue(property);
                            }
                            else
                            {
                                row[col.ColumnName] = DBNull.Value;
                            }
                        }
                        dataTable.Rows.Add(row);
                    }
                }
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in root.EnumerateObject())
                {
                    if (!dataTable.Columns.Contains(property.Name))
                        dataTable.Columns.Add(property.Name, GetColumnType(property.Value));
                }
                var row = dataTable.NewRow();
                foreach (var property in root.EnumerateObject())
                {
                    row[property.Name] = GetValue(property.Value);
                }
                dataTable.Rows.Add(row);
            }
            else if (root.ValueKind == JsonValueKind.String || root.ValueKind == JsonValueKind.Number || root.ValueKind == JsonValueKind.True || root.ValueKind == JsonValueKind.False)
            {
                dataTable.Columns.Add("Value", GetColumnType(root));
                var row = dataTable.NewRow();
                row[0] = GetValue(root);
                dataTable.Rows.Add(row);
            }
            else if (root.ValueKind == JsonValueKind.Null)
            {
                dataTable.Columns.Add("Value", typeof(string));
                var row = dataTable.NewRow();
                row[0] = DBNull.Value;
                dataTable.Rows.Add(row);
            }
            return dataTable;
        }
        catch (Exception ex)
        {
            return new Error($"Failed to convert JSON to DataTable: {ex.Message}", ex.StackTrace);
        }
    }

    public static Result<List<Dictionary<string, object>>> ProcessDataTableToNestedJson(DataTable dataTable, JsonNestingConfig? config = null)
    {
        config ??= JsonNestingConfig.Default;
        var result = new List<Dictionary<string, object>>(dataTable.Rows.Count);

        try
        {
            var nestedColumns = new HashSet<string>();
            foreach (DataColumn column in dataTable.Columns)
            {
                if (config.ShouldNestProperty(column.ColumnName))
                {
                    nestedColumns.Add(column.ColumnName);
                }
            }

            foreach (DataRow row in dataTable.Rows)
            {
                var nestedRow = new Dictionary<string, object>(dataTable.Columns.Count);

                foreach (DataColumn column in dataTable.Columns)
                {
                    var value = row[column];
                    if (value == DBNull.Value) continue;

                    if (nestedColumns.Contains(column.ColumnName) && value is string stringValue)
                    {
                        nestedRow[column.ColumnName] = ProcessJsonStringValue(stringValue);
                    }
                    else
                    {
                        nestedRow[column.ColumnName] = value;
                    }
                }

                result.Add(nestedRow);
            }

            return result;
        }
        catch (Exception ex)
        {
            return new Error($"Failed to convert to nested JSON: {ex.Message}", ex.StackTrace);
        }
    }

    public static bool IsJsonString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        value = value.Trim();
        return (value.StartsWith('[') && value.EndsWith(']')) ||
               (value.StartsWith('{') && value.EndsWith('}'));
    }

    private static object ProcessJsonStringValue(string stringValue)
    {
        if (!IsJsonString(stringValue))
            return stringValue;

        try
        {
            if (stringValue.Trim().StartsWith('['))
            {
                var parsed = JsonSerializer.Deserialize<object[]>(stringValue, JsonOptions);
                return parsed ?? Array.Empty<object>();
            }
            else
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, object>>(stringValue, JsonOptions);
                return parsed ?? new Dictionary<string, object>();
            }
        }
        catch
        {
            return stringValue;
        }
    }

    private static Type GetColumnType(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => typeof(string),
            JsonValueKind.Number => element.TryGetInt32(out _) ? typeof(int) :
                                   element.TryGetInt64(out _) ? typeof(long) : typeof(double),
            JsonValueKind.True or JsonValueKind.False => typeof(bool),
            JsonValueKind.Null => typeof(string),
            _ => typeof(string)
        };
    }

    private static object GetValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString()!,
            JsonValueKind.Number => element.TryGetInt32(out var intVal) ? intVal :
                                   element.TryGetInt64(out var longVal) ? longVal :
                                   element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => DBNull.Value,
            JsonValueKind.Array => element.ToString(),
            JsonValueKind.Object => element.ToString(),
            _ => element.ToString()
        };
    }
}