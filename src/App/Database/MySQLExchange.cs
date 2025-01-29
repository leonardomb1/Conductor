using System.Data;
using System.Data.Common;
using System.Text;
using Conductor.Logging;
using Conductor.Model;
using Conductor.Shared.Config;
using Conductor.Shared.Types;
using MySql.Data.MySqlClient;

namespace Conductor.App.Database;

public class MySQLExchange : DBExchange
{
    protected override string? QueryPagination(UInt64 current) =>
        $"LIMIT {Settings.ProducerLineMax} OFFSET {current}";

    protected override string? QueryNonLocking() => "LOCK IN SHARE MODE";

    protected override string GeneratePartitionCondition(Extraction extraction, double timeZoneOffSet, string? virtualColumn = null)
    {
        StringBuilder builder = new();
        if (!extraction.FilterTime.HasValue)
        {
            throw new Exception("Filter time cannot be null in this context.");
        }

        if (extraction.FilterTime.HasValue)
        {
            var lookupTime = DateTime.UtcNow.AddSeconds((double)-extraction.FilterTime!).AddHours(timeZoneOffSet);
            builder.Append($"AND \"{extraction.FilterColumn}\" >= '{lookupTime:yyyy-MM-dd HH:mm:ss}' ");
        }

        if (extraction.VirtualId != null && virtualColumn != null)
        {
            builder.Append($"OR \"{virtualColumn}\" = '{extraction.VirtualId}' ");
        }

        return builder.ToString();
    }

    protected override StringBuilder AddSurrogateKey(StringBuilder stringBuilder, string index, string tableName, string? file)
    {
        string indexGroup = file == null ? $"{index} ASC" : $"{index} ASC, {tableName}_{Settings.IndexFileGroupName} ASC";
        return stringBuilder.Append($" UNIQUE ({indexGroup}),");
    }

    protected override StringBuilder AddChangeColumn(StringBuilder stringBuilder, string tableName) =>
        stringBuilder.AppendLine($" DT_UPDATE_{tableName} DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,");

    protected override StringBuilder AddColumnarStructure(StringBuilder stringBuilder, string tableName) =>
        stringBuilder.Append($"");

    protected override StringBuilder AddIdentityColumn(StringBuilder stringBuilder, string tableName) =>
        stringBuilder.AppendLine($" ID_DW_{tableName} INT AUTO_INCREMENT,");

    protected override async Task EnsureSchemaCreation(string system, DbConnection connection)
    {
        using MySqlCommand select = new($"SELECT SCHEMA_NAME FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = '{system}'", (MySqlConnection)connection);
        var res = await select.ExecuteScalarAsync();

        if (res == null)
        {
            Log.Out($"Creating schema {system}...");
            using MySqlCommand createSchema = new($"CREATE SCHEMA {system}", (MySqlConnection)connection);
            await createSchema.ExecuteNonQueryAsync();
        }
    }

    public override DbConnection CreateConnection(string conStr)
    {
        return new MySqlConnection(conStr);
    }

    protected override DbCommand CreateDbCommand(string query, DbConnection connection)
    {
        return new MySqlCommand(query, (MySqlConnection)connection);
    }

    protected override string GetSqlType(Type type, Int32? length = -1)
    {
        return type switch
        {
            _ when type == typeof(Int64) => "BIGINT",
            _ when type == typeof(Int32) => "INT",
            _ when type == typeof(Int16) => "SMALLINT",
            _ when type == typeof(string) => length > 0 ? $"VARCHAR({length})" : "TEXT",
            _ when type == typeof(bool) => "TINYINT(1)",
            _ when type == typeof(DateTime) => "DATETIME",
            _ when type == typeof(double) => "DOUBLE",
            _ when type == typeof(decimal) => "DECIMAL(18,2)",
            _ when type == typeof(byte) => "TINYINT UNSIGNED",
            _ when type == typeof(sbyte) => "TINYINT",
            _ when type == typeof(UInt16) => "SMALLINT UNSIGNED",
            _ when type == typeof(UInt32) => "INT UNSIGNED",
            _ when type == typeof(UInt64) => "BIGINT UNSIGNED",
            _ when type == typeof(float) => "FLOAT",
            _ when type == typeof(char) => "CHAR(1)",
            _ when type == typeof(Guid) => "CHAR(36)",
            _ when type == typeof(TimeSpan) => "TIME",
            _ when type == typeof(byte[]) => "BLOB",
            _ when type == typeof(object) => "JSON",
            _ => throw new NotSupportedException($"Type '{type.Name}' is not supported")
        };
    }

    public override async Task<Result> WriteDataTable(DataTable data, Extraction extraction)
    {
        string tableName = extraction.Alias ?? extraction.Name;

        try
        {
            using MySqlConnection connection = new(extraction.Destination!.ConnectionString);
            await connection.OpenAsync();

            using MySqlTransaction transaction = await connection.BeginTransactionAsync();

            MySqlBulkLoader bulk = new(connection)
            {
                TableName = $"{tableName}",
                FieldTerminator = ",",
                LineTerminator = "\n",
                NumberOfLinesToSkip = 0,
                Local = true
            };

            foreach (DataColumn column in data.Columns)
            {
                bulk.Columns.Add(column.ColumnName);
            }

            using MemoryStream memoryStream = new();
            using StreamWriter writer = new(memoryStream, Encoding.UTF8, 1024, true);
            foreach (DataRow row in data.Rows)
            {
                var fields = row.ItemArray.Select(field => field?.ToString());
                writer.WriteLine(string.Join(",", fields));
            }

            memoryStream.Position = 0;
            using StreamReader reader = new(memoryStream);
            bulk.Load(reader.BaseStream);
            Log.Out($"Writing row data {data.Rows.Count} - in {bulk.TableName}");
            await bulk.LoadAsync();

            await transaction.CommitAsync();
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
        string tableName = extraction.Alias ?? extraction.Name;

        try
        {
            using MySqlConnection mySQLConnection = (MySqlConnection)connection;
            using MySqlTransaction transaction = await mySQLConnection.BeginTransactionAsync();

            MySqlBulkLoader bulk = new(mySQLConnection)
            {
                TableName = $"{tableName}",
                FieldTerminator = ",",
                LineTerminator = "\n",
                NumberOfLinesToSkip = 0,
                Local = true
            };

            foreach (DataColumn column in data.Columns)
            {
                bulk.Columns.Add(column.ColumnName);
            }

            using MemoryStream memoryStream = new();
            using StreamWriter writer = new(memoryStream, Encoding.UTF8, 1024, true);
            foreach (DataRow row in data.Rows)
            {
                var fields = row.ItemArray.Select(field => field?.ToString());
                writer.WriteLine(string.Join(",", fields));
            }

            memoryStream.Position = 0;
            using StreamReader reader = new(memoryStream);
            bulk.Load(reader.BaseStream);
            Log.Out($"Writing row data {data.Rows.Count} - in {bulk.TableName}");
            await bulk.LoadAsync();

            await transaction.CommitAsync();
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }
}
