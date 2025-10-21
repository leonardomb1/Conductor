using System.Data;
using System.Data.Common;
using System.Text;
using Conductor.Model;
using Conductor.Shared;
using Conductor.Types;
using ClickHouse.Client.ADO;
using ClickHouse.Client.Copy;
using Serilog;

namespace Conductor.Service.Database;

public class ClickHouseExchange : DBExchange
{
    protected override string? QueryPagination(ulong rows, ulong limit) =>
        $"LIMIT {limit} OFFSET {rows}";

    protected override string? QueryNonLocking() => ""; // ClickHouse doesn't require locking hints

    protected override string GeneratePartitionCondition(Extraction extraction, DateTime requestTime, string? virtualColumn = null, int? effectiveFilterTime = null)
    {
        StringBuilder builder = new();
        
        var filterTime = effectiveFilterTime ?? extraction.FilterTime;
        if (filterTime.HasValue && extraction.Origin?.TimeZoneOffSet.HasValue == true)
        {
            var lookupTime = RequestTimeWithOffSet(requestTime, filterTime.Value, extraction.Origin.TimeZoneOffSet.Value);
            builder.Append($"AND `{extraction.FilterColumn}` >= '{lookupTime:yyyy-MM-dd HH:mm:ss}' ");
        }

        if (extraction.VirtualId is not null && virtualColumn is not null)
        {
            builder.Append($"AND `{virtualColumn}` = '{extraction.VirtualId}' ");
        }

        return builder.ToString();
    }

    protected override StringBuilder AddSurrogateKey(
            StringBuilder stringBuilder,
            string index,
            string tableName,
            string? virtualIdGroup
        )
    {
        // ClickHouse doesn't support traditional unique constraints
        // Instead, we'll note this in comments - uniqueness is enforced by the ORDER BY in the table engine
        string indexGroup = virtualIdGroup is null ? $"`{index}`" : $"`{index}`, `{tableName}_{virtualIdGroup}`";
        return stringBuilder.Append($" -- Surrogate key: {indexGroup}");
    }

    protected override StringBuilder AddChangeColumn(StringBuilder stringBuilder, string tableName) =>
        stringBuilder.AppendLine($" `DT_UPDATE_{tableName}` DateTime DEFAULT now(),");

    protected override StringBuilder AddIdentityColumn(StringBuilder stringBuilder, string tableName) =>
        stringBuilder.AppendLine($" `ID_DW_{tableName}` UInt64,");

    protected override StringBuilder AddPrimaryKey(StringBuilder stringBuilder, string tableName)
    {
        // ClickHouse doesn't use PRIMARY KEY constraint in the same way
        // Primary key is defined in the ENGINE clause
        return stringBuilder;
    }

    protected override async Task EnsureSchemaCreation(string system, DbConnection connection)
    {
        // ClickHouse uses databases instead of schemas
        // Use inline parameter substitution instead of parameterized query
        using var select = new ClickHouseCommand(connection as ClickHouseConnection)
        {
            CommandText = $"SELECT name FROM system.databases WHERE name = '{system}'"
        };

        var res = await select.ExecuteScalarAsync();

        if (res is null)
        {
            Log.Information($"Creating database {system}...");
            using var createDatabase = new ClickHouseCommand(connection as ClickHouseConnection)
            {
                CommandText = $"CREATE DATABASE IF NOT EXISTS `{system}`"
            };
            await createDatabase.ExecuteNonQueryAsync();
        }
    }

    public override DbConnection CreateConnection(string conStr)
    {
        return new ClickHouseConnection(conStr);
    }

    protected override DbCommand CreateDbCommand(string query, DbConnection connection)
    {
        return new ClickHouseCommand(connection as ClickHouseConnection)
        {
            CommandText = query
        };
    }

    protected override string GetSqlType(Type type, int? length = -1)
    {
        return type switch
        {
            _ when type == typeof(long) => "Int64",
            _ when type == typeof(int) => "Int32",
            _ when type == typeof(short) => "Int16",
            _ when type == typeof(string) => "String",
            _ when type == typeof(bool) => "UInt8", // ClickHouse doesn't have native boolean
            _ when type == typeof(DateTime) => "DateTime",
            _ when type == typeof(DateTimeOffset) => "DateTime64(3)",
            _ when type == typeof(DateOnly) => "Date",
            _ when type == typeof(TimeOnly) => "String", // ClickHouse doesn't have native Time type
            _ when type == typeof(double) => "Float64",
            _ when type == typeof(decimal) => "Decimal(18,2)",
            _ when type == typeof(byte) => "UInt8",
            _ when type == typeof(sbyte) => "Int8",
            _ when type == typeof(ushort) => "UInt16",
            _ when type == typeof(uint) => "UInt32",
            _ when type == typeof(ulong) => "UInt64",
            _ when type == typeof(float) => "Float32",
            _ when type == typeof(char) => "FixedString(1)",
            _ when type == typeof(Guid) => "UUID",
            _ when type == typeof(TimeSpan) => "Int64", // Store as ticks
            _ when type == typeof(byte[]) => "String", // Binary data as String
            _ when type == typeof(object) => "String", // JSON as String
            // Handle nullable types - ClickHouse uses Nullable() wrapper
            _ when type == typeof(DateTime?) => "Nullable(DateTime)",
            _ when type == typeof(DateTimeOffset?) => "Nullable(DateTime64(3))",
            _ when type == typeof(DateOnly?) => "Nullable(Date)",
            _ when type == typeof(TimeOnly?) => "Nullable(String)",
            _ when type == typeof(int?) => "Nullable(Int32)",
            _ when type == typeof(long?) => "Nullable(Int64)",
            _ when type == typeof(short?) => "Nullable(Int16)",
            _ when type == typeof(double?) => "Nullable(Float64)",
            _ when type == typeof(decimal?) => "Nullable(Decimal(18,2))",
            _ when type == typeof(bool?) => "Nullable(UInt8)",
            _ when type == typeof(byte?) => "Nullable(UInt8)",
            _ when type == typeof(sbyte?) => "Nullable(Int8)",
            _ when type == typeof(ushort?) => "Nullable(UInt16)",
            _ when type == typeof(uint?) => "Nullable(UInt32)",
            _ when type == typeof(ulong?) => "Nullable(UInt64)",
            _ when type == typeof(float?) => "Nullable(Float32)",
            _ when type == typeof(char?) => "Nullable(FixedString(1))",
            _ when type == typeof(Guid?) => "Nullable(UUID)",
            _ when type == typeof(TimeSpan?) => "Nullable(Int64)",
            _ => throw new NotSupportedException($"Type '{type.Name}' is not supported for ClickHouse")
        };
    }

    public override async Task<Result> CreateTable(DataTable table, Extraction extraction, DbConnection connection)
    {
        if (extraction.Origin is null) return new Error($"No origin was given for {extraction.Name}, id: {extraction.Id}");
        if (extraction.IndexName is null) return new Error("Invalid metadata, missing index name.");
        
        string schemaName = extraction.Origin.Alias ?? extraction.Origin.Name;
        string tableName = extraction.Alias ?? extraction.Name;

        var queryBuilder = new StringBuilder();

        queryBuilder.AppendLine($"CREATE TABLE IF NOT EXISTS `{schemaName}`.`{tableName}` (");

        foreach (DataColumn column in table.Columns)
        {
            int? maxStringLength = column.MaxLength;
            string SqlType = GetSqlType(column.DataType, maxStringLength);
            queryBuilder.AppendLine($"    `{column.ColumnName}` {SqlType},");
        }
        
        queryBuilder = AddChangeColumn(queryBuilder, tableName);
        queryBuilder = AddIdentityColumn(queryBuilder, tableName);

        // Remove trailing comma and newline
        string current = queryBuilder.ToString();
        if (current.TrimEnd().EndsWith(','))
        {
            // Find the last comma and remove it along with any trailing whitespace
            int lastCommaIndex = current.LastIndexOf(',');
            queryBuilder.Length = lastCommaIndex;
            queryBuilder.AppendLine();
        }

        // ClickHouse requires an ENGINE clause
        // Using MergeTree which is the most common and versatile
        string orderByClause = extraction.VirtualIdGroup is not null 
            ? $"`{extraction.IndexName}`, `{tableName}_{extraction.VirtualIdGroup}`"
            : $"`{extraction.IndexName}`";
            
        queryBuilder.AppendLine($") ENGINE = MergeTree()");
        queryBuilder.AppendLine($"ORDER BY ({orderByClause})");
        queryBuilder.AppendLine($"SETTINGS index_granularity = 8192;");

        try
        {
            await EnsureSchemaCreation(schemaName, connection);

            Log.Information($"Creating table {schemaName}.{tableName}...");
            using var command = CreateDbCommand(queryBuilder.ToString(), connection);
            await command.ExecuteNonQueryAsync();

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    public override async Task<Result> MergeLoad(DataTable data, Extraction extraction, DateTime requestTime, DbConnection connection)
    {
        if (extraction.Origin?.Alias is null && extraction.Origin?.Name is null)
        {
            return new Error("Extraction origin alias or name is required");
        }

        string schemaName = extraction.Origin!.Alias ?? extraction.Origin!.Name;
        string tableName = extraction.Alias ?? extraction.Name;
        string virtualColumn = extraction.IsVirtual ? VirtualColumn(tableName, extraction.VirtualIdGroup ?? "file") : "";

        try
        {
            var clickHouseConnection = (ClickHouseConnection)connection;

            // ClickHouse doesn't support traditional MERGE/UPSERT
            // Strategy: Use ReplacingMergeTree or manual delete + insert
            // For simplicity, we'll do a conditional delete and insert
            
            // First, insert new data
            await BulkLoad(data, extraction, connection);

            // Then handle deletions if this is incremental sync with filter
            if (extraction.FilterTime.HasValue && 
                extraction.Origin?.TimeZoneOffSet.HasValue == true && 
                !string.IsNullOrEmpty(extraction.FilterColumn))
            {
                var lookupTime = RequestTimeWithOffSet(requestTime, extraction.FilterTime.Value, extraction.Origin.TimeZoneOffSet.Value);

                // ClickHouse ALTER TABLE DELETE is used for deletions
                StringBuilder deleteQuery = new StringBuilder();
                deleteQuery.AppendLine($"ALTER TABLE `{schemaName}`.`{tableName}` DELETE WHERE");
                deleteQuery.AppendLine($"`{extraction.FilterColumn}` >= '{lookupTime:yyyy-MM-dd HH:mm:ss}'");
                deleteQuery.AppendLine($"AND `{extraction.IndexName}` NOT IN (");
                deleteQuery.AppendLine($"    SELECT `{extraction.IndexName}` FROM `{schemaName}`.`{tableName}`");
                deleteQuery.AppendLine($"    WHERE `{extraction.FilterColumn}` >= '{lookupTime:yyyy-MM-dd HH:mm:ss}'");
                deleteQuery.AppendLine(")");

                if (extraction.IsVirtual && !string.IsNullOrEmpty(virtualColumn))
                {
                    deleteQuery.AppendLine($"AND `{virtualColumn}` = '{extraction.VirtualId}'");
                }

                using var deleteCommand = CreateDbCommand(deleteQuery.ToString(), connection);
                deleteCommand.CommandTimeout = Settings.QueryTimeout;

                await deleteCommand.ExecuteNonQueryAsync();
                Log.Information($"Executed delete query for unsynced rows in {schemaName}.{tableName}");
            }

            Log.Information($"Merge operation completed for {schemaName}.{tableName}");
            return Result.Ok();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Merge operation failed for table {SchemaName}.{TableName}", schemaName, tableName);
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    public override async Task<Result> BulkLoad(DataTable data, Extraction extraction)
    {
        if (extraction.Origin?.Alias is null && extraction.Origin?.Name is null)
        {
            return new Error("Extraction origin alias or name is required");
        }

        if (extraction.Destination?.ConnectionString is null)
        {
            return new Error("Destination connection string is required");
        }

        string schemaName = extraction.Origin!.Alias ?? extraction.Origin!.Name;
        string tableName = extraction.Alias ?? extraction.Name;

        try
        {
            using var connection = new ClickHouseConnection(extraction.Destination!.ConnectionString);
            await connection.OpenAsync();

            await BulkLoad(data, extraction, connection);

            await connection.CloseAsync();

            Log.Information($"Bulk loaded {data.Rows.Count} rows into {schemaName}.{tableName}");
            return Result.Ok();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Bulk load failed for table {SchemaName}.{TableName}", schemaName, tableName);
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    public override async Task<Result> BulkLoad(DataTable data, Extraction extraction, DbConnection connection)
    {
        if (extraction.Origin?.Alias is null && extraction.Origin?.Name is null)
        {
            return new Error("Extraction origin alias or name is required");
        }

        string schemaName = extraction.Origin!.Alias ?? extraction.Origin!.Name;
        string tableName = extraction.Alias ?? extraction.Name;

        try
        {
            var clickHouseConnection = (ClickHouseConnection)connection;

            // Use ClickHouse's native bulk copy functionality
            using var bulkCopy = new ClickHouseBulkCopy(clickHouseConnection)
            {
                DestinationTableName = $"`{schemaName}`.`{tableName}`",
                BatchSize = 100000
            };

            // Convert DataTable to IDataReader for ClickHouseBulkCopy
            using var reader = data.CreateDataReader();
            await bulkCopy.WriteToServerAsync(reader);

            Log.Information($"Bulk loaded {data.Rows.Count} rows into {schemaName}.{tableName}");
            return Result.Ok();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Bulk load failed for table {SchemaName}.{TableName}", schemaName, tableName);
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    public override async Task<Result<bool>> Exists(Extraction extraction, DbConnection connection)
    {
        string schemaName = extraction.Origin!.Alias ?? extraction.Origin!.Name;
        string tableName = extraction.Alias ?? extraction.Name;

        using DbCommand command = CreateDbCommand(
            $"EXISTS TABLE `{schemaName}`.`{tableName}`",
            connection
        );

        command.CommandTimeout = Settings.QueryTimeout;

        try
        {
            var res = await command.ExecuteScalarAsync();
            return res is not null && Convert.ToInt32(res) == 1;
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    public override async Task<Result<ulong>> CountTableRows(Extraction extraction, DbConnection connection)
    {
        string schemaName = extraction.Origin!.Alias ?? extraction.Origin!.Name;
        string tableName = extraction.Alias ?? extraction.Name;

        using DbCommand command = CreateDbCommand(
            $"SELECT COUNT(*) FROM `{schemaName}`.`{tableName}`",
            connection
        );

        command.CommandTimeout = Settings.QueryTimeout;

        try
        {
            var res = await command.ExecuteScalarAsync();
            return res == DBNull.Value ? 0 : Convert.ToUInt64(res);
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    public override async Task<Result> TruncateTable(Extraction extraction, DbConnection connection)
    {
        string schemaName = extraction.Origin!.Alias ?? extraction.Origin!.Name;
        string tableName = extraction.Alias ?? extraction.Name;

        using DbCommand command = CreateDbCommand(
            $"TRUNCATE TABLE `{schemaName}`.`{tableName}`",
            connection
        );

        command.CommandTimeout = Settings.QueryTimeout;

        try
        {
            Log.Information($"Truncating table {schemaName}.{tableName}...");
            await command.ExecuteNonQueryAsync();
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    protected override async Task<Result<List<string>>> GetColumnInformation(DbConnection connection, string tableName, CancellationToken token)
    {
        if (token.IsCancellationRequested) return new Error("Operation Cancelled.");

        using DbCommand command = CreateDbCommand(
            $"SELECT name FROM system.columns WHERE table = '{tableName}'",
            connection
        );
        command.CommandTimeout = Settings.QueryTimeout;

        List<string> columns = [];

        try
        {
            using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                columns.Add(reader.GetString(0));
            }

            return columns;
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }
}
