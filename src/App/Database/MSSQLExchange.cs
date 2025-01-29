using System.Data;
using System.Data.Common;
using System.Text;
using Conductor.Logging;
using Conductor.Model;
using Conductor.Shared.Config;
using Conductor.Shared.Types;
using Microsoft.Data.SqlClient;

namespace Conductor.App.Database;

public class MSSQLExchange : DBExchange
{
    protected override string? QueryPagination(UInt64 current) =>
        $"OFFSET {current} ROWS FETCH NEXT {Settings.ProducerLineMax} ROWS ONLY";

    protected override string? QueryNonLocking() => "WITH(NOLOCK)";

    protected override string GeneratePartitionCondition(Extraction extraction, double timeZoneOffSet, string? virtualColumn = null)
    {
        StringBuilder builder = new();

        builder.Append("WHERE 1 = 1 ");

        if (extraction.FilterTime.HasValue)
        {
            var lookupTime = DateTime.UtcNow.AddSeconds((double)-extraction.FilterTime!).AddHours(timeZoneOffSet);
            builder.Append($"AND \"{extraction.FilterColumn}\" >= CAST('{lookupTime:yyyy-MM-dd HH:mm:ss}' AS DATETIME2) ");
        }

        if (extraction.VirtualId != null && virtualColumn != null)
        {
            builder.Append($"AND \"{virtualColumn}\" = '{extraction.VirtualId}' ");
        }

        return builder.ToString();
    }

    protected override StringBuilder AddSurrogateKey(StringBuilder stringBuilder, string index, string tableName, string? file)
    {
        string indexGroup = file == null ? $"{index} ASC" : $"{index} ASC, {tableName}_{Settings.IndexFileGroupName} ASC";
        return stringBuilder.Append($" CONSTRAINT IX_{tableName}_SK UNIQUE NONCLUSTERED ({indexGroup}),");
    }

    protected override StringBuilder AddChangeColumn(StringBuilder stringBuilder, string tableName) =>
        stringBuilder.AppendLine($" DT_UPDATE_{tableName} DATETIME NOT NULL CONSTRAINT CK_UPDATE_{tableName} DEFAULT (GETDATE()),");

    protected override StringBuilder AddColumnarStructure(StringBuilder stringBuilder, string tableName) =>
        stringBuilder.Append($" INDEX IX_{tableName}_CCI CLUSTERED COLUMNSTORE");

    protected override StringBuilder AddIdentityColumn(StringBuilder stringBuilder, string tableName) =>
        stringBuilder.AppendLine($" ID_DW_{tableName} INT IDENTITY(1,1),");

    protected override async Task EnsureSchemaCreation(string system, DbConnection connection)
    {
        using SqlCommand select = new("SELECT SCHEMA_NAME FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = @schema", (SqlConnection)connection);
        select.Parameters.AddWithValue("@schema", system);

        var res = await select.ExecuteScalarAsync();

        if (res == DBNull.Value || res == null)
        {
            Log.Out($"Creating schema {system}...");
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

    protected override string GetSqlType(Type type, Int32? length = -1)
    {
        return type switch
        {
            _ when type == typeof(Int64) => "BIGINT",
            _ when type == typeof(Int32) => "INT",
            _ when type == typeof(Int16) => "SMALLINT",
            _ when type == typeof(string) => length > 0 ? $"VARCHAR({length})" : "VARCHAR(MAX)",
            _ when type == typeof(bool) => "BIT",
            _ when type == typeof(DateTime) => "DATETIME",
            _ when type == typeof(double) => "FLOAT",
            _ when type == typeof(decimal) => "DECIMAL(18,2)",
            _ when type == typeof(byte) => "TINYINT",
            _ when type == typeof(sbyte) => "TINYINT",
            _ when type == typeof(UInt16) => "SMALLINT",
            _ when type == typeof(UInt32) => "INT",
            _ when type == typeof(UInt64) => "BIGINT",
            _ when type == typeof(float) => "REAL",
            _ when type == typeof(char) => "NCHAR(1)",
            _ when type == typeof(Guid) => "UNIQUEIDENTIFIER",
            _ when type == typeof(TimeSpan) => "TIME",
            _ when type == typeof(byte[]) => "VARBINARY(MAX)",
            _ when type == typeof(object) => "SQL_VARIANT",
            _ => throw new NotSupportedException($"Type '{type.Name}' is not supported")
        };
    }

    public override async Task<Result> WriteDataTable(DataTable data, Extraction extraction)
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

            Log.Out($"Writing imported row data with {data.Rows.Count} lines on table: {bulk.DestinationTableName}");

            await bulk.WriteToServerAsync(data);

            await connection.CloseAsync();

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    public override async Task<Result> WriteDataTable(DataTable data, Extraction extraction, DbConnection connection)
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

            Log.Out($"Writing imported row data with {data.Rows.Count} lines on table: {bulk.DestinationTableName}");

            await bulk.WriteToServerAsync(data);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }
}