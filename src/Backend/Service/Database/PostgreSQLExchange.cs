using System.Data;
using System.Data.Common;
using System.Text;
using Conductor.Model;
using Conductor.Types;
using Npgsql;
using Serilog;

namespace Conductor.Service.Database;

public class PostgreSQLExchange : DBExchange
{
    protected override string? QueryPagination(ulong rows, ulong limit) =>
        $"OFFSET {rows} LIMIT {limit}";

    protected override string? QueryNonLocking() => "";

    protected override string GeneratePartitionCondition(Extraction extraction, DateTime requestTime, string? virtualColumn = null)
    {
        StringBuilder builder = new();

        if (!extraction.FilterTime.HasValue)
        {
            throw new Exception("Filter time cannot be null in this context.");
        }

        if (extraction.FilterTime.HasValue)
        {
            var lookupTime = requestTime.AddSeconds((double)-extraction.FilterTime!).AddHours(extraction.Origin!.TimeZoneOffSet!.Value);
            builder.Append($"AND \"{extraction.FilterColumn}\" >= '{lookupTime:yyyy-MM-dd HH:mm:ss}'::TIMESTAMP ");
        }

        if (extraction.VirtualId is not null && virtualColumn is not null)
        {
            builder.Append($"OR \"{virtualColumn}\" = '{extraction.VirtualId}' ");
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
        string indexGroup = (virtualIdGroup is null || virtualIdGroup == "") ? $"{index}" : $"{index}, {tableName}_{virtualIdGroup}";
        return stringBuilder.Append($" UNIQUE (\"{indexGroup}\")");
    }

    protected override StringBuilder AddChangeColumn(StringBuilder stringBuilder, string tableName) =>
        stringBuilder.AppendLine($" \"DT_UPDATE_{tableName}\" TIMESTAMPTZ NOT NULL DEFAULT NOW(),");

    protected override StringBuilder AddIdentityColumn(StringBuilder stringBuilder, string tableName) =>
        stringBuilder.AppendLine($" ID_DW_{tableName} INT GENERATED ALWAYS AS IDENTITY,");

    protected override async Task EnsureSchemaCreation(string system, DbConnection connection)
    {
        using var select = new NpgsqlCommand("SELECT schema_name FROM information_schema.schemata WHERE schema_name = @schema", (NpgsqlConnection)connection);
        select.Parameters.AddWithValue("@schema", system);

        var res = await select.ExecuteScalarAsync();

        if (res == DBNull.Value || res is null)
        {
            Log.Information($"Creating schema {system}...");
            using var createSchema = new NpgsqlCommand($"CREATE SCHEMA IF NOT EXISTS \"{system}\"", (NpgsqlConnection)connection);
            await createSchema.ExecuteNonQueryAsync();
        }
    }

    public override DbConnection CreateConnection(string conStr)
    {
        return new NpgsqlConnection(conStr);
    }

    protected override DbCommand CreateDbCommand(string query, DbConnection connection)
    {
        return new NpgsqlCommand(query, (NpgsqlConnection)connection);
    }

    protected override string GetSqlType(Type type, int? length = -1)
    {
        return type switch
        {
            _ when type == typeof(long) => "BIGINT",
            _ when type == typeof(int) => "INTEGER",
            _ when type == typeof(short) => "SMALLINT",
            _ when type == typeof(string) => length > 0 ? (length > 10485760 ? "TEXT" : $"VARCHAR({length})") : "TEXT",
            _ when type == typeof(bool) => "BOOLEAN",
            _ when type == typeof(DateTime) => "TIMESTAMPTZ",
            _ when type == typeof(double) => "DOUBLE PRECISION",
            _ when type == typeof(decimal) => "NUMERIC(18,2)",
            _ when type == typeof(byte) => "SMALLINT",
            _ when type == typeof(sbyte) => "SMALLINT",
            _ when type == typeof(ushort) => "INTEGER",
            _ when type == typeof(uint) => "BIGINT",
            _ when type == typeof(ulong) => "NUMERIC",
            _ when type == typeof(float) => "REAL",
            _ when type == typeof(char) => "CHAR(1)",
            _ when type == typeof(Guid) => "UUID",
            _ when type == typeof(TimeSpan) => "INTERVAL",
            _ when type == typeof(byte[]) => "BYTEA",
            _ when type == typeof(object) => "JSONB",
            _ => throw new NotSupportedException($"Type '{type.Name}' is not supported")
        };
    }

    public override Task<Result> MergeLoad(DataTable data, Extraction extraction, DateTime requestTime, DbConnection connection)
    {
        throw new NotImplementedException();
    }

    public override async Task<Result> BulkLoad(DataTable data, Extraction extraction)
    {
        string schemaName = extraction.Origin!.Alias ?? extraction.Origin!.Name;
        string tableName = extraction.Alias ?? extraction.Name;

        try
        {
            using var connection = new NpgsqlConnection(extraction.Destination!.ConnectionString);
            await connection.OpenAsync();

            string columns = string.Join(", ", data.Columns.Cast<DataColumn>().Select(c => $"\"{c.ColumnName}\""));

            using var writer = connection.BeginBinaryImport(
                $"COPY \"{schemaName}\".\"{tableName}\" ({columns}) FROM STDIN (FORMAT BINARY)"
            );

            foreach (DataRow row in data.Rows)
            {
                writer.StartRow();
                foreach (var item in row.ItemArray)
                {
                    writer.Write(item);
                }
            }

            await writer.CompleteAsync();
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
            string columns = string.Join(", ", data.Columns.Cast<DataColumn>().Select(c => $"\"{c.ColumnName}\""));

            using var npgsqlConnection = (NpgsqlConnection)connection;

            using var writer = npgsqlConnection.BeginBinaryImport(
                $"COPY \"{schemaName}\".\"{tableName}\" ({columns}) FROM STDIN (FORMAT BINARY)"
            );

            foreach (DataRow row in data.Rows)
            {
                writer.StartRow();
                foreach (var item in row.ItemArray)
                {
                    writer.Write(item);
                }
            }

            await writer.CompleteAsync();
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }
}
