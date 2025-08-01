using System.Data;
using System.Data.Common;
using System.Text;
using Conductor.Model;
using Conductor.Shared;
using Conductor.Types;
using Microsoft.Data.SqlClient;
using Serilog;

namespace Conductor.Service.Database;

public class MSSQLExchange : DBExchange
{
    protected override string? QueryPagination(ulong rows, ulong limit) =>
        $"OFFSET {rows} ROWS FETCH NEXT {limit} ROWS ONLY";

    protected override string? QueryNonLocking() => "WITH(NOLOCK)";

    protected override string GeneratePartitionCondition(Extraction extraction, DateTime requestTime, string? virtualColumn = null, int? effectiveFilterTime = null)
    {
        StringBuilder builder = new();

        var filterTime = effectiveFilterTime ?? extraction.FilterTime;
        if (filterTime.HasValue && extraction.Origin?.TimeZoneOffSet.HasValue == true)
        {
            var lookupTime = RequestTimeWithOffSet(requestTime, filterTime.Value, extraction.Origin.TimeZoneOffSet.Value);
            builder.Append($"AND \"{extraction.FilterColumn}\" >= CAST('{lookupTime:yyyy-MM-dd HH:mm:ss}' AS DATETIME2) ");
        }

        if (extraction.VirtualId is not null && virtualColumn is not null)
        {
            builder.Append($"AND \"{virtualColumn}\" = '{extraction.VirtualId}' ");
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
        string indexGroup = virtualIdGroup is null ? $"{index} ASC" : $"{index} ASC, {tableName}_{virtualIdGroup} ASC";
        return stringBuilder.Append($" CONSTRAINT IX_{tableName}_SK UNIQUE NONCLUSTERED ({indexGroup}),");
    }

    protected override StringBuilder AddChangeColumn(StringBuilder stringBuilder, string tableName) =>
        stringBuilder.AppendLine($" DT_UPDATE_{tableName} DATETIME NOT NULL CONSTRAINT CK_UPDATE_{tableName} DEFAULT (GETDATE()),");

    protected override StringBuilder AddIdentityColumn(StringBuilder stringBuilder, string tableName) =>
        stringBuilder.AppendLine($" ID_DW_{tableName} INT IDENTITY(1,1),");

    protected override async Task EnsureSchemaCreation(string system, DbConnection connection)
    {
        using SqlCommand select = new("SELECT SCHEMA_NAME FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = @schema", (SqlConnection)connection);
        select.Parameters.AddWithValue("@schema", system);

        var res = await select.ExecuteScalarAsync();

        if (res == DBNull.Value || res is null)
        {
            Log.Information($"Creating schema {system}...");
            using SqlCommand createSchema = new($"CREATE SCHEMA {system}", (SqlConnection)connection);
            await createSchema.ExecuteNonQueryAsync();
        }
    }

    public override DbConnection CreateConnection(string conStr)
    {
        return new SqlConnection(conStr);
    }

    protected override DbCommand CreateDbCommand(string query, DbConnection connection)
    {
        return new SqlCommand(query, (SqlConnection)connection);
    }

    protected override string GetSqlType(Type type, int? length = -1)
    {
        return type switch
        {
            _ when type == typeof(long) => "BIGINT",
            _ when type == typeof(int) => "INT",
            _ when type == typeof(short) => "SMALLINT",
            _ when type == typeof(string) => length > 0 && length < 8000 ? $"VARCHAR({length})" : "VARCHAR(MAX)",
            _ when type == typeof(bool) => "BIT",
            _ when type == typeof(DateTime) => "DATETIME2",
            _ when type == typeof(DateTimeOffset) => "DATETIMEOFFSET",
            _ when type == typeof(DateOnly) => "DATE",
            _ when type == typeof(TimeOnly) => "TIME",
            _ when type == typeof(double) => "FLOAT",
            _ when type == typeof(decimal) => "DECIMAL(18,2)",
            _ when type == typeof(byte) => "TINYINT",
            _ when type == typeof(sbyte) => "TINYINT",
            _ when type == typeof(ushort) => "SMALLINT",
            _ when type == typeof(uint) => "INT",
            _ when type == typeof(ulong) => "BIGINT",
            _ when type == typeof(float) => "REAL",
            _ when type == typeof(char) => "NCHAR(1)",
            _ when type == typeof(Guid) => "UNIQUEIDENTIFIER",
            _ when type == typeof(TimeSpan) => "TIME",
            _ when type == typeof(byte[]) => "VARBINARY(MAX)",
            _ when type == typeof(object) => "SQL_VARIANT",
            // Handle nullable types
            _ when type == typeof(DateTime?) => "DATETIME2",
            _ when type == typeof(DateTimeOffset?) => "DATETIMEOFFSET",
            _ when type == typeof(DateOnly?) => "DATE",
            _ when type == typeof(TimeOnly?) => "TIME",
            _ when type == typeof(int?) => "INT",
            _ when type == typeof(long?) => "BIGINT",
            _ when type == typeof(short?) => "SMALLINT",
            _ when type == typeof(double?) => "FLOAT",
            _ when type == typeof(decimal?) => "DECIMAL(18,2)",
            _ when type == typeof(bool?) => "BIT",
            _ when type == typeof(byte?) => "TINYINT",
            _ when type == typeof(sbyte?) => "TINYINT",
            _ when type == typeof(ushort?) => "SMALLINT",
            _ when type == typeof(uint?) => "INT",
            _ when type == typeof(ulong?) => "BIGINT",
            _ when type == typeof(float?) => "REAL",
            _ when type == typeof(char?) => "NCHAR(1)",
            _ when type == typeof(Guid?) => "UNIQUEIDENTIFIER",
            _ when type == typeof(TimeSpan?) => "TIME",
            _ => throw new NotSupportedException($"Type '{type.Name}' is not supported")
        };
    }

    public override async Task<Result> MergeLoad(DataTable data, Extraction extraction, DateTime requestTime, DbConnection connection)
    {
        if (extraction.Origin?.Alias is null && extraction.Origin?.Name is null)
        {
            return new Error("Extraction origin alias or name is required");
        }

        if (extraction.FilterTime is null && extraction.Origin?.TimeZoneOffSet is null)
        {
            return new Error("FilterTime and TimeZoneOffset are required for merge operations");
        }

        string schemaName = extraction.Origin!.Alias ?? extraction.Origin!.Name;
        string tableName = extraction.Alias ?? extraction.Name;
        string virtualColumn = extraction.IsVirtual ? VirtualColumn(tableName, extraction.VirtualIdGroup ?? "file") : "";

        var tempTableName = $"#Temp_{tableName}_{Guid.NewGuid():N}";

        try
        {
            using var dropTempTableCommand = CreateDbCommand($"DROP TABLE IF EXISTS \"{tempTableName}\"", connection);
            await dropTempTableCommand.ExecuteNonQueryAsync();

            var createTempTableQuery = new StringBuilder();
            createTempTableQuery.AppendLine($"CREATE TABLE \"{tempTableName}\" (");

            var columnDefinitions = new List<string>();
            foreach (DataColumn column in data.Columns)
            {
                int? maxStringLength = column.MaxLength > 0 ? column.MaxLength : null;
                string sqlType = GetSqlType(column.DataType, maxStringLength);
                columnDefinitions.Add($"    \"{column.ColumnName}\" {sqlType}");
            }

            createTempTableQuery.AppendLine(string.Join(",\n", columnDefinitions));
            createTempTableQuery.AppendLine(");");

            using var createTempTableCommand = CreateDbCommand(createTempTableQuery.ToString(), connection);
            await createTempTableCommand.ExecuteNonQueryAsync();

            using var bulkCopy = new SqlBulkCopy((SqlConnection)connection)
            {
                DestinationTableName = tempTableName,
                BulkCopyTimeout = Settings.QueryTimeout
            };

            await bulkCopy.WriteToServerAsync(data);

            var mergeQuery = new StringBuilder();
            mergeQuery.AppendLine($"MERGE INTO \"{schemaName}\".\"{tableName}\" AS Target");
            mergeQuery.AppendLine($"USING \"{tempTableName}\" AS Source");
            mergeQuery.AppendLine($"ON Target.\"{extraction.IndexName}\" = Source.\"{extraction.IndexName}\"");

            if (extraction.IsVirtual && !string.IsNullOrEmpty(virtualColumn))
            {
                mergeQuery.AppendLine($"AND Target.\"{virtualColumn}\" = Source.\"{virtualColumn}\" ");
            }

            mergeQuery.AppendLine("WHEN MATCHED THEN");
            mergeQuery.AppendLine("    UPDATE SET");

            var updateColumns = data.Columns.Cast<DataColumn>()
                .Where(column => column.ColumnName != virtualColumn && column.ColumnName != extraction.IndexName)
                .Select(column => $"Target.\"{column.ColumnName}\" = Source.\"{column.ColumnName}\"");
            mergeQuery.AppendLine(string.Join(",\n        ", updateColumns));

            mergeQuery.AppendLine("WHEN NOT MATCHED BY Target THEN");
            mergeQuery.AppendLine("    INSERT (");

            var insertColumns = data.Columns.Cast<DataColumn>()
                .Select(column => $"\"{column.ColumnName}\"");
            mergeQuery.AppendLine(string.Join(",\n        ", insertColumns));

            mergeQuery.AppendLine("    ) VALUES (");

            var values = data.Columns.Cast<DataColumn>()
                .Select(column => $"Source.\"{column.ColumnName}\"");
            mergeQuery.AppendLine(string.Join(",\n        ", values));
            mergeQuery.AppendLine("    );");

            Log.Information($"Upserting source data for table {schemaName}.{tableName}...");
            using var mergeCommand = CreateDbCommand(mergeQuery.ToString(), connection);
            mergeCommand.CommandTimeout = Settings.QueryTimeout;

            var affectedRows = await mergeCommand.ExecuteNonQueryAsync();
            Log.Information($"Merge operation affected {affectedRows} rows in {schemaName}.{tableName}");

            // Delete unsynced data
            var lookupTime = RequestTimeWithOffSet(requestTime, extraction.FilterTime!.Value, extraction.Origin!.TimeZoneOffSet!.Value);

            StringBuilder deleteQuery = new($"DELETE FROM \"{schemaName}\".\"{tableName}\"");
            deleteQuery.AppendLine("WHERE NOT EXISTS (");
            deleteQuery.AppendLine($"    SELECT 1 FROM \"{tempTableName}\"");
            deleteQuery.AppendLine($"    WHERE \"{tempTableName}\".\"{extraction.IndexName}\" = \"{schemaName}\".\"{tableName}\".\"{extraction.IndexName}\"");

            if (extraction.IsVirtual && !string.IsNullOrEmpty(virtualColumn))
            {
                deleteQuery.AppendLine($"    AND \"{tempTableName}\".\"{virtualColumn}\" = \"{schemaName}\".\"{tableName}\".\"{virtualColumn}\"");
            }

            deleteQuery.AppendLine(")");

            if (!string.IsNullOrEmpty(extraction.FilterColumn))
            {
                deleteQuery.AppendLine($"AND \"{extraction.FilterColumn}\" >= CAST('{lookupTime:yyyy-MM-dd HH:mm:ss}' AS DATETIME2);");
            }

            using var deleteCommand = CreateDbCommand(deleteQuery.ToString(), connection);
            deleteCommand.CommandTimeout = Settings.QueryTimeout;

            var deletedRows = await deleteCommand.ExecuteNonQueryAsync();
            Log.Information($"Deleted {deletedRows} unsynced rows from {schemaName}.{tableName}");

            return Result.Ok();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Merge operation failed for table {SchemaName}.{TableName}", schemaName, tableName);
            return new Error(ex.Message, ex.StackTrace);
        }
        finally
        {
            try
            {
                using var dropTempTableCommand = CreateDbCommand($"DROP TABLE IF EXISTS \"{tempTableName}\"", connection);
                await dropTempTableCommand.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to clean up temp table {TempTableName}", tempTableName);
            }
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
            using SqlConnection connection = new(extraction.Destination!.ConnectionString);
            await connection.OpenAsync();

            using var bulk = new SqlBulkCopy(connection)
            {
                BulkCopyTimeout = Settings.QueryTimeout,
                DestinationTableName = $"{schemaName}.{tableName}"
            };

            await bulk.WriteToServerAsync(data);
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
            var bulkCopyOptions = SqlBulkCopyOptions.TableLock |
                                  SqlBulkCopyOptions.UseInternalTransaction |
                                  SqlBulkCopyOptions.KeepNulls;


            using var bulk = new SqlBulkCopy((SqlConnection)connection, bulkCopyOptions, null)
            {
                BulkCopyTimeout = Settings.QueryTimeout,
                DestinationTableName = $"{schemaName}.{tableName}"
            };

            await bulk.WriteToServerAsync(data);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Bulk load failed for table {SchemaName}.{TableName}", schemaName, tableName);
            return new Error(ex.Message, ex.StackTrace);
        }
    }
}