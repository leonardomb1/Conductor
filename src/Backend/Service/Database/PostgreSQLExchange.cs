using System.Data;
using System.Data.Common;
using System.Text;
using Conductor.Model;
using Conductor.Shared;
using Conductor.Types;
using Npgsql;
using Serilog;

namespace Conductor.Service.Database;

public class PostgreSQLExchange : DBExchange
{
    protected override string? QueryPagination(ulong rows, ulong limit) =>
        $"OFFSET {rows} LIMIT {limit}";

    protected override string? QueryNonLocking() => "";

    protected override string GeneratePartitionCondition(Extraction extraction, DateTime requestTime, string? virtualColumn = null, int? effectiveFilterTime = null)
    {
        StringBuilder builder = new();

        var filterTime = effectiveFilterTime ?? extraction.FilterTime;
        if (filterTime.HasValue && extraction.Origin?.TimeZoneOffSet.HasValue == true)
        {
            // FIXED: Now uses consistent RequestTimeWithOffSet method
            var lookupTime = RequestTimeWithOffSet(requestTime, filterTime.Value, extraction.Origin.TimeZoneOffSet.Value);
            builder.Append($"AND \"{extraction.FilterColumn}\" >= '{lookupTime:yyyy-MM-dd HH:mm:ss}'::TIMESTAMP ");
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
        string indexGroup = (virtualIdGroup is null || virtualIdGroup == "") ? $"\"{index}\"" : $"\"{index}\", \"{tableName}_{virtualIdGroup}\"";
        return stringBuilder.Append($" UNIQUE ({indexGroup})");
    }

    protected override StringBuilder AddChangeColumn(StringBuilder stringBuilder, string tableName) =>
        stringBuilder.AppendLine($" \"DT_UPDATE_{tableName}\" TIMESTAMPTZ NOT NULL DEFAULT NOW(),");

    protected override StringBuilder AddIdentityColumn(StringBuilder stringBuilder, string tableName) =>
        stringBuilder.AppendLine($" \"ID_DW_{tableName}\" SERIAL,");

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

        var tempTableName = $"temp_{tableName}_{Guid.NewGuid():N}".Replace("-", "_");

        try
        {
            using var npgsqlConnection = (NpgsqlConnection)connection;
            using var transaction = await npgsqlConnection.BeginTransactionAsync();

            // Create temporary table
            var createTempTableQuery = new StringBuilder();
            createTempTableQuery.AppendLine($"CREATE TEMP TABLE \"{tempTableName}\" (");

            foreach (DataColumn column in data.Columns)
            {
                int? maxStringLength = column.MaxLength;
                string sqlType = GetSqlType(column.DataType, maxStringLength);
                createTempTableQuery.AppendLine($"    \"{column.ColumnName}\" {sqlType},");
            }

            // Remove trailing comma and close table definition
            if (createTempTableQuery.Length > 0)
            {
                createTempTableQuery.Length -= 3; // Remove ",\r\n"
                createTempTableQuery.AppendLine();
            }
            createTempTableQuery.AppendLine(");");

            using var createTempTableCommand = CreateDbCommand(createTempTableQuery.ToString(), connection);
            await createTempTableCommand.ExecuteNonQueryAsync();

            // Load data into temporary table using COPY
            string columns = string.Join(", ", data.Columns.Cast<DataColumn>().Select(c => $"\"{c.ColumnName}\""));

            using var writer = npgsqlConnection.BeginBinaryImport(
                $"COPY \"{tempTableName}\" ({columns}) FROM STDIN (FORMAT BINARY)"
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

            // Perform UPSERT using INSERT ... ON CONFLICT
            var upsertQuery = new StringBuilder();
            upsertQuery.AppendLine($"INSERT INTO \"{schemaName}\".\"{tableName}\" (");
            
            var insertColumns = data.Columns.Cast<DataColumn>()
                .Select(column => $"\"{column.ColumnName}\"");
            upsertQuery.AppendLine(string.Join(",\n    ", insertColumns));
            
            upsertQuery.AppendLine(")");
            upsertQuery.AppendLine($"SELECT ");
            
            var selectColumns = data.Columns.Cast<DataColumn>()
                .Select(column => $"\"{column.ColumnName}\"");
            upsertQuery.AppendLine(string.Join(",\n    ", selectColumns));
            
            upsertQuery.AppendLine($"FROM \"{tempTableName}\"");
            
            // Use the index column for conflict resolution
            upsertQuery.AppendLine($"ON CONFLICT (\"{extraction.IndexName}\"{(extraction.IsVirtual && !string.IsNullOrEmpty(virtualColumn) ? $", \"{virtualColumn}\"" : "")})");
            upsertQuery.AppendLine("DO UPDATE SET");
            
            var updateColumns = data.Columns.Cast<DataColumn>()
                .Where(column => column.ColumnName != virtualColumn && column.ColumnName != extraction.IndexName)
                .Select(column => $"\"{column.ColumnName}\" = EXCLUDED.\"{column.ColumnName}\"");
            upsertQuery.AppendLine(string.Join(",\n    ", updateColumns));

            Log.Information($"Upserting data for table {schemaName}.{tableName}...");
            using var upsertCommand = CreateDbCommand(upsertQuery.ToString(), connection);
            upsertCommand.CommandTimeout = Settings.QueryTimeout;

            var affectedRows = await upsertCommand.ExecuteNonQueryAsync();
            Log.Information($"Upsert operation affected {affectedRows} rows in {schemaName}.{tableName}");

            // Delete unsynced data
            if (extraction.FilterTime.HasValue && extraction.Origin?.TimeZoneOffSet.HasValue == true && !string.IsNullOrEmpty(extraction.FilterColumn))
            {
                var lookupTime = RequestTimeWithOffSet(requestTime, extraction.FilterTime.Value, extraction.Origin.TimeZoneOffSet.Value);

                StringBuilder deleteQuery = new($"DELETE FROM \"{schemaName}\".\"{tableName}\"");
                deleteQuery.AppendLine("WHERE NOT EXISTS (");
                deleteQuery.AppendLine($"    SELECT 1 FROM \"{tempTableName}\"");
                deleteQuery.AppendLine($"    WHERE \"{tempTableName}\".\"{extraction.IndexName}\" = \"{schemaName}\".\"{tableName}\".\"{extraction.IndexName}\"");

                if (extraction.IsVirtual && !string.IsNullOrEmpty(virtualColumn))
                {
                    deleteQuery.AppendLine($"    AND \"{tempTableName}\".\"{virtualColumn}\" = \"{schemaName}\".\"{tableName}\".\"{virtualColumn}\"");
                }

                deleteQuery.AppendLine(")");
                deleteQuery.AppendLine($"AND \"{extraction.FilterColumn}\" >= '{lookupTime:yyyy-MM-dd HH:mm:ss}'::TIMESTAMP;");

                using var deleteCommand = CreateDbCommand(deleteQuery.ToString(), connection);
                deleteCommand.CommandTimeout = Settings.QueryTimeout;

                var deletedRows = await deleteCommand.ExecuteNonQueryAsync();
                Log.Information($"Deleted {deletedRows} unsynced rows from {schemaName}.{tableName}");
            }

            await transaction.CommitAsync();
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
            Log.Information($"Bulk loaded {data.Rows.Count} rows into {schemaName}.{tableName}");
            return Result.Ok();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Bulk load failed for table {SchemaName}.{TableName}", schemaName, tableName);
            return new Error(ex.Message, ex.StackTrace);
        }
    }
}