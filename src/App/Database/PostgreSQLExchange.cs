using System.Data;
using System.Data.Common;
using System.Text;
using Conductor.Model;
using Conductor.Shared.Config;
using Conductor.Shared.Types;
using Npgsql;

namespace Conductor.App.Database;

public class PostgreSQLExchange : DBExchange
{
    protected override string? QueryPagination(UInt64 current) =>
        $"OFFSET {current} LIMIT {Settings.ProducerLineMax}";

    protected override string? QueryNonLocking() => "FOR SHARE";

    protected override StringBuilder AddPrimaryKey(StringBuilder stringBuilder, string index, string tableName, string? file)
    {
        string indexGroup = (file == null || file == "") ? $"{index}" : $"{index}, {tableName}_EMPRESA";
        return stringBuilder.Append($" PRIMARY KEY ({indexGroup})");
    }

    protected override StringBuilder AddChangeColumn(StringBuilder stringBuilder, string tableName) =>
        stringBuilder.AppendLine($" DT_UPDATE_{tableName} TIMESTAMPTZ NOT NULL DEFAULT NOW(),");

    protected override StringBuilder AddColumnarStructure(StringBuilder stringBuilder, string tableName) =>
        stringBuilder.Append($"");

    protected override async Task<bool> LookupTable(string tableName, DbConnection connection)
    {
        using var select = new NpgsqlCommand("SELECT to_regclass(@table)::text", (NpgsqlConnection)connection);
        select.Parameters.AddWithValue("@table", tableName);

        var res = await select.ExecuteScalarAsync();

        return res != DBNull.Value && res != null;
    }

    protected override async Task EnsureSchemaCreation(string system, DbConnection connection)
    {
        using var select = new NpgsqlCommand("SELECT schema_name FROM information_schema.schemata WHERE schema_name = @schema", (NpgsqlConnection)connection);
        select.Parameters.AddWithValue("@schema", system);

        var res = await select.ExecuteScalarAsync();

        if (res == DBNull.Value || res == null)
        {
            using var createSchema = new NpgsqlCommand($"CREATE SCHEMA {system}", (NpgsqlConnection)connection);
            await createSchema.ExecuteNonQueryAsync();
        }
    }

    protected override DbConnection CreateConnection(string conStr)
    {
        return new NpgsqlConnection(conStr);
    }

    protected override DbCommand CreateDbCommand(string query, DbConnection connection)
    {
        return new NpgsqlCommand(query, (NpgsqlConnection)connection);
    }

    protected override string GetSqlType(Type type, Int32? length = -1)
    {
        return type switch
        {
            _ when type == typeof(Int64) => "BIGINT",
            _ when type == typeof(Int32) => "INTEGER",
            _ when type == typeof(Int16) => "SMALLINT",
            _ when type == typeof(string) => length > 0 ? $"VARCHAR({length})" : "TEXT",
            _ when type == typeof(bool) => "BOOLEAN",
            _ when type == typeof(DateTime) => "TIMESTAMPTZ",
            _ when type == typeof(double) => "DOUBLE PRECISION",
            _ when type == typeof(decimal) => "NUMERIC(18,2)",
            _ when type == typeof(byte) => "SMALLINT",
            _ when type == typeof(sbyte) => "SMALLINT",
            _ when type == typeof(UInt16) => "INTEGER",
            _ when type == typeof(UInt32) => "BIGINT",
            _ when type == typeof(UInt64) => "NUMERIC",
            _ when type == typeof(float) => "REAL",
            _ when type == typeof(char) => "CHAR(1)",
            _ when type == typeof(Guid) => "UUID",
            _ when type == typeof(TimeSpan) => "INTERVAL",
            _ when type == typeof(byte[]) => "BYTEA",
            _ when type == typeof(object) => "JSONB",
            _ => throw new NotSupportedException($"Type '{type.Name}' is not supported")
        };
    }

    protected override async Task<Result> BulkInsert(DataTable data, Extraction extraction)
    {
        try
        {
            using var connection = new NpgsqlConnection(extraction.Destination!.DbString);
            await connection.OpenAsync();

            using var writer = connection.BeginBinaryImport($"COPY {extraction.Origin!.Name}.{extraction.Name} FROM STDIN (FORMAT BINARY)");

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
}
