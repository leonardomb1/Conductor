using System.Data;
using System.Data.Common;
using System.Text;
using Conductor.Model;
using Conductor.Shared;
using Conductor.Types;
using MySql.Data.MySqlClient;
using Serilog;

namespace Conductor.Service.Database;

public class MySQLExchange : DBExchange
{
    protected override string? QueryPagination(ulong rows, ulong limit) =>
        $"LIMIT {limit} OFFSET {rows}";

    protected override string? QueryNonLocking() => "LOCK IN SHARE MODE";

    protected override string GeneratePartitionCondition(Extraction extraction, DateTime requestTime, string? virtualColumn = null, int? effectiveFilterTime = null)
    {
        StringBuilder builder = new();
        
        var filterTime = effectiveFilterTime ?? extraction.FilterTime;
        if (filterTime.HasValue && extraction.Origin?.TimeZoneOffSet.HasValue == true)
        {
            // FIXED: Now uses consistent RequestTimeWithOffSet method
            var lookupTime = RequestTimeWithOffSet(requestTime, filterTime.Value, extraction.Origin.TimeZoneOffSet.Value);
            builder.Append($"AND \"{extraction.FilterColumn}\" >= '{lookupTime:yyyy-MM-dd HH:mm:ss}' ");
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
        return stringBuilder.Append($" UNIQUE ({indexGroup}),");
    }

    protected override StringBuilder AddChangeColumn(StringBuilder stringBuilder, string tableName) =>
        stringBuilder.AppendLine($" DT_UPDATE_{tableName} DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,");

    protected override StringBuilder AddIdentityColumn(StringBuilder stringBuilder, string tableName) =>
        stringBuilder.AppendLine($" ID_DW_{tableName} INT AUTO_INCREMENT,");

    protected override async Task EnsureSchemaCreation(string system, DbConnection connection)
    {
        using MySqlCommand select = new($"SELECT SCHEMA_NAME FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = '{system}'", (MySqlConnection)connection);
        var res = await select.ExecuteScalarAsync();

        if (res is null)
        {
            Log.Information($"Creating schema {system}...");
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

    protected override string GetSqlType(Type type, int? length = -1)
    {
        return type switch
        {
            _ when type == typeof(long) => "BIGINT",
            _ when type == typeof(int) => "INT",
            _ when type == typeof(short) => "SMALLINT",
            _ when type == typeof(string) => length > 0 ? $"VARCHAR({length})" : "TEXT",
            _ when type == typeof(bool) => "TINYINT(1)",
            _ when type == typeof(DateTime) => "DATETIME",
            _ when type == typeof(double) => "DOUBLE",
            _ when type == typeof(decimal) => "DECIMAL(18,2)",
            _ when type == typeof(byte) => "TINYINT UNSIGNED",
            _ when type == typeof(sbyte) => "TINYINT",
            _ when type == typeof(ushort) => "SMALLINT UNSIGNED",
            _ when type == typeof(uint) => "INT UNSIGNED",
            _ when type == typeof(ulong) => "BIGINT UNSIGNED",
            _ when type == typeof(float) => "FLOAT",
            _ when type == typeof(char) => "CHAR(1)",
            _ when type == typeof(Guid) => "CHAR(36)",
            _ when type == typeof(TimeSpan) => "TIME",
            _ when type == typeof(byte[]) => "BLOB",
            _ when type == typeof(object) => "JSON",
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

        var tempTableName = $"temp_{tableName}_{Guid.NewGuid():N}".Replace("-", "");

        try
        {
            using var mySqlConnection = (MySqlConnection)connection;
            using var transaction = await mySqlConnection.BeginTransactionAsync();

            var createTempTableQuery = new StringBuilder();
            createTempTableQuery.AppendLine($"CREATE TEMPORARY TABLE `{tempTableName}` (");

            foreach (DataColumn column in data.Columns)
            {
                int? maxStringLength = column.MaxLength;
                string sqlType = GetSqlType(column.DataType, maxStringLength);
                createTempTableQuery.AppendLine($"    `{column.ColumnName}` {sqlType},");
            }

            if (createTempTableQuery.Length > 0)
            {
                createTempTableQuery.Length -= 3;
                createTempTableQuery.AppendLine();
            }
            createTempTableQuery.AppendLine(");");

            using var createTempTableCommand = CreateDbCommand(createTempTableQuery.ToString(), connection);
            await createTempTableCommand.ExecuteNonQueryAsync();

            MySqlBulkLoader bulk = new(mySqlConnection)
            {
                TableName = tempTableName,
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
                var fields = row.ItemArray.Select(field => field?.ToString()?.Replace(",", "\\,") ?? "");
                writer.WriteLine(string.Join(",", fields));
            }

            memoryStream.Position = 0;
            using StreamReader reader = new(memoryStream);
            
            bulk.Load(reader.BaseStream);
            await bulk.LoadAsync();

            var upsertQuery = new StringBuilder();
            upsertQuery.AppendLine($"INSERT INTO `{schemaName}`.`{tableName}` (");
            
            var insertColumns = data.Columns.Cast<DataColumn>()
                .Select(column => $"`{column.ColumnName}`");
            upsertQuery.AppendLine(string.Join(",\n    ", insertColumns));
            
            upsertQuery.AppendLine(")");
            upsertQuery.AppendLine($"SELECT ");
            
            var selectColumns = data.Columns.Cast<DataColumn>()
                .Select(column => $"`{column.ColumnName}`");
            upsertQuery.AppendLine(string.Join(",\n    ", selectColumns));
            
            upsertQuery.AppendLine($"FROM `{tempTableName}`");
            upsertQuery.AppendLine("ON DUPLICATE KEY UPDATE");
            
            var updateColumns = data.Columns.Cast<DataColumn>()
                .Where(column => column.ColumnName != virtualColumn && column.ColumnName != extraction.IndexName)
                .Select(column => $"`{column.ColumnName}` = VALUES(`{column.ColumnName}`)");
            upsertQuery.AppendLine(string.Join(",\n    ", updateColumns));

            Log.Information($"Upserting data for table {schemaName}.{tableName}...");
            using var upsertCommand = CreateDbCommand(upsertQuery.ToString(), connection);
            upsertCommand.CommandTimeout = Settings.QueryTimeout;

            var affectedRows = await upsertCommand.ExecuteNonQueryAsync();
            Log.Information($"Upsert operation affected {affectedRows} rows in {schemaName}.{tableName}");

            if (extraction.FilterTime.HasValue && extraction.Origin?.TimeZoneOffSet.HasValue == true && !string.IsNullOrEmpty(extraction.FilterColumn))
            {
                var lookupTime = RequestTimeWithOffSet(requestTime, extraction.FilterTime.Value, extraction.Origin.TimeZoneOffSet.Value);

                StringBuilder deleteQuery = new($"DELETE FROM `{schemaName}`.`{tableName}`");
                deleteQuery.AppendLine("WHERE NOT EXISTS (");
                deleteQuery.AppendLine($"    SELECT 1 FROM `{tempTableName}`");
                deleteQuery.AppendLine($"    WHERE `{tempTableName}`.`{extraction.IndexName}` = `{schemaName}`.`{tableName}`.`{extraction.IndexName}`");

                if (extraction.IsVirtual && !string.IsNullOrEmpty(virtualColumn))
                {
                    deleteQuery.AppendLine($"    AND `{tempTableName}`.`{virtualColumn}` = `{schemaName}`.`{tableName}`.`{virtualColumn}`");
                }

                deleteQuery.AppendLine(")");
                deleteQuery.AppendLine($"AND `{extraction.FilterColumn}` >= '{lookupTime:yyyy-MM-dd HH:mm:ss}';");

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
                var fields = row.ItemArray.Select(field => field?.ToString()?.Replace(",", "\\,") ?? "");
                writer.WriteLine(string.Join(",", fields));
            }

            memoryStream.Position = 0;
            using StreamReader reader = new(memoryStream);
            bulk.Load(reader.BaseStream);
            
            Log.Information($"Bulk loading {data.Rows.Count} rows into {bulk.TableName}");
            await bulk.LoadAsync();

            await transaction.CommitAsync();
            await connection.CloseAsync();

            Log.Information($"Bulk loaded {data.Rows.Count} rows into {tableName}");
            return Result.Ok();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Bulk load failed for table {TableName}", tableName);
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    public override async Task<Result> BulkLoad(DataTable data, Extraction extraction, DbConnection connection)
    {
        if (extraction.Origin?.Alias is null && extraction.Origin?.Name is null)
        {
            return new Error("Extraction origin alias or name is required");
        }

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
                var fields = row.ItemArray.Select(field => field?.ToString()?.Replace(",", "\\,") ?? "");
                writer.WriteLine(string.Join(",", fields));
            }

            memoryStream.Position = 0;
            using StreamReader reader = new(memoryStream);
            bulk.Load(reader.BaseStream);
            
            Log.Information($"Bulk loading {data.Rows.Count} rows into {bulk.TableName}");
            await bulk.LoadAsync();

            await transaction.CommitAsync();
            Log.Information($"Bulk loaded {data.Rows.Count} rows into {tableName}");
            return Result.Ok();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Bulk load failed for table {TableName}", tableName);
            return new Error(ex.Message, ex.StackTrace);
        }
    }
}