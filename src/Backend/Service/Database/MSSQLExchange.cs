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

    protected override string GeneratePartitionCondition(Extraction extraction, DateTime requestTime, string? virtualColumn = null)
    {
        StringBuilder builder = new();

        if (extraction.FilterTime.HasValue)
        {
            var lookupTime = RequestTimeWithOffSet(requestTime, (int)extraction.FilterTime, extraction.Origin!.TimeZoneOffSet!.Value);
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
            _ when type == typeof(DateTime) => "DATETIME",
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
            _ => throw new NotSupportedException($"Type '{type.Name}' is not supported")
        };
    }

    public override async Task<Result> MergeLoad(DataTable data, Extraction extraction, DateTime requestTime, DbConnection connection)
    {
        string schemaName = extraction.Origin!.Alias ?? extraction.Origin!.Name;
        string tableName = extraction.Alias ?? extraction.Name;
        string virtualColumn = extraction.IsVirtual ? VirtualColumn(tableName, extraction.VirtualIdGroup ?? "file") : "";

        var tempTableName = $"#Temp_{tableName}";

        var createTempTableQuery = new StringBuilder();

        using var dropTempTableCommand = CreateDbCommand($"DROP TABLE IF EXISTS \"{tempTableName}\"", connection);
        await dropTempTableCommand.ExecuteNonQueryAsync();

        createTempTableQuery.AppendLine($"CREATE TABLE \"{tempTableName}\" (");

        foreach (DataColumn column in data.Columns)
        {
            int? maxStringLength = column.MaxLength;
            string sqlType = GetSqlType(column.DataType, maxStringLength);
            createTempTableQuery.AppendLine($"    \"{column.ColumnName}\" {sqlType},");
        }

        createTempTableQuery.Length--;
        createTempTableQuery.AppendLine(");");

        try
        {
            using var createTempTableCommand = CreateDbCommand(createTempTableQuery.ToString(), connection);
            await createTempTableCommand.ExecuteNonQueryAsync();

            using var bulkCopy = new SqlBulkCopy((SqlConnection)connection)
            {
                DestinationTableName = tempTableName
            };

            await bulkCopy.WriteToServerAsync(data);

            var mergeQuery = new StringBuilder();
            mergeQuery.AppendLine($"MERGE INTO \"{schemaName}\".\"{tableName}\" AS Target");
            mergeQuery.AppendLine($"USING \"{tempTableName}\" AS Source");
            mergeQuery.AppendLine($"ON Target.\"{extraction.IndexName}\" = Source.\"{extraction.IndexName}\"");

            if (extraction.IsVirtual)
            {
                mergeQuery.AppendLine($"AND Target.\"{virtualColumn}\" = Source.\"{virtualColumn}\" ");
            }

            mergeQuery.AppendLine("WHEN MATCHED THEN");
            mergeQuery.AppendLine("    UPDATE SET");

            var updateColumns = data.Columns.Cast<DataColumn>()
                .Where(column => column.ColumnName != virtualColumn && column.ColumnName != extraction.IndexName)
                .Select(column => $"Target.\"{column.ColumnName}\" = Source.\"{column.ColumnName}\"");
            mergeQuery.AppendLine(string.Join(",\n    ", updateColumns));

            mergeQuery.AppendLine("WHEN NOT MATCHED BY Target THEN");
            mergeQuery.AppendLine("    INSERT (");

            var insertColumns = data.Columns.Cast<DataColumn>()
                .Select(column => $"\"{column.ColumnName}\"");
            mergeQuery.AppendLine(string.Join(",\n    ", insertColumns));

            mergeQuery.AppendLine("    ) VALUES (");

            var values = data.Columns.Cast<DataColumn>()
                .Select(column => $"Source.\"{column.ColumnName}\"");
            mergeQuery.AppendLine(string.Join(",\n    ", values));
            mergeQuery.AppendLine("    );");

            var lookupTime = RequestTimeWithOffSet(requestTime, (int)extraction.FilterTime!, extraction.Origin!.TimeZoneOffSet!.Value);

            Log.Information($"Upserting source data and deleting unsynced data on table {schemaName}.{tableName}...");
            using var mergeCommand = CreateDbCommand(mergeQuery.ToString(), connection);
            mergeCommand.CommandTimeout = Settings.QueryTimeout;

            await mergeCommand.ExecuteNonQueryAsync();

            StringBuilder deleteQuery = new($"DELETE FROM \"{schemaName}\".\"{tableName}\"");
            deleteQuery.AppendLine("WHERE NOT EXISTS (");
            deleteQuery.AppendLine($"SELECT 1 FROM \"{tempTableName}\"");
            deleteQuery.AppendLine($"WHERE \"{tempTableName}\".\"{extraction.IndexName}\" = \"{schemaName}\".\"{tableName}\".\"{extraction.IndexName}\"");

            if (extraction.IsVirtual)
            {
                deleteQuery.AppendLine($"AND \"{tempTableName}\".\"{virtualColumn}\" = \"{schemaName}\".\"{tableName}\".\"{virtualColumn}\"");
            }

            deleteQuery.AppendLine(")");
            deleteQuery.AppendLine($"AND \"{extraction.FilterColumn}\" >= CAST('{lookupTime:yyyy-MM-dd HH:mm:ss}' AS DATETIME2);");

            using var deleteCommand = CreateDbCommand(deleteQuery.ToString(), connection);
            deleteCommand.CommandTimeout = Settings.QueryTimeout;

            await deleteCommand.ExecuteNonQueryAsync();

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
        finally
        {
            await dropTempTableCommand.ExecuteNonQueryAsync();
        }
    }

    public override async Task<Result> BulkLoad(DataTable data, Extraction extraction)
    {
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

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    public override async Task<Result> BulkLoad(DataTable data, Extraction extraction, DbConnection connection)
    {
        string schemaName = extraction.Origin!.Alias ?? extraction.Origin!.Name;
        string tableName = extraction.Alias ?? extraction.Name;

        try
        {
            using var bulk = new SqlBulkCopy((SqlConnection)connection)
            {
                BulkCopyTimeout = Settings.QueryTimeout,
                DestinationTableName = $"{schemaName}.{tableName}"
            };

            await bulk.WriteToServerAsync(data);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }
}