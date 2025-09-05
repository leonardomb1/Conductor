using System.Data;
using System.Data.Common;
using System.Text;
using Conductor.Model;
using Conductor.Shared;
using Conductor.Types;
using MySqlConnector;
using Serilog;

namespace Conductor.Service.Database;

public class MySQLExchange : DBExchange
{
    protected override string? QueryPagination(ulong rows, ulong limit) =>
        $"LIMIT {limit} OFFSET {rows}";

    protected override string? QueryNonLocking() => ""; // No locking hints needed for MySQL

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
        string indexGroup = virtualIdGroup is null ? $"`{index}` ASC" : $"`{index}` ASC, `{tableName}_{virtualIdGroup}` ASC";
        return stringBuilder.Append($" UNIQUE KEY `IX_{tableName}_SK` ({indexGroup}),");
    }

    protected override StringBuilder AddChangeColumn(StringBuilder stringBuilder, string tableName) =>
        stringBuilder.AppendLine($" `DT_UPDATE_{tableName}` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,");

    protected override StringBuilder AddIdentityColumn(StringBuilder stringBuilder, string tableName) =>
        stringBuilder.AppendLine($" `ID_DW_{tableName}` INT AUTO_INCREMENT,");

    protected override async Task EnsureSchemaCreation(string system, DbConnection connection)
    {
        using var select = new MySqlCommand($"SELECT SCHEMA_NAME FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = @schema", (MySqlConnection)connection);
        select.Parameters.AddWithValue("@schema", system);
        var res = await select.ExecuteScalarAsync();

        if (res is null)
        {
            Log.Information($"Creating schema {system}...");
            using var createSchema = new MySqlCommand($"CREATE SCHEMA IF NOT EXISTS `{system}`", (MySqlConnection)connection);
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
            _ when type == typeof(string) => length > 0 && length <= 65535 ? $"VARCHAR({length})" : "TEXT",
            _ when type == typeof(bool) => "TINYINT(1)",
            _ when type == typeof(DateTime) => "DATETIME",
            _ when type == typeof(DateTimeOffset) => "DATETIME",
            _ when type == typeof(DateOnly) => "DATE",
            _ when type == typeof(TimeOnly) => "TIME",
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
            _ when type == typeof(byte[]) => "LONGBLOB",
            _ when type == typeof(object) => "JSON",
            // Handle nullable types
            _ when type == typeof(DateTime?) => "DATETIME",
            _ when type == typeof(DateTimeOffset?) => "DATETIME",
            _ when type == typeof(DateOnly?) => "DATE",
            _ when type == typeof(TimeOnly?) => "TIME",
            _ when type == typeof(int?) => "INT",
            _ when type == typeof(long?) => "BIGINT",
            _ when type == typeof(short?) => "SMALLINT",
            _ when type == typeof(double?) => "DOUBLE",
            _ when type == typeof(decimal?) => "DECIMAL(18,2)",
            _ when type == typeof(bool?) => "TINYINT(1)",
            _ when type == typeof(byte?) => "TINYINT UNSIGNED",
            _ when type == typeof(sbyte?) => "TINYINT",
            _ when type == typeof(ushort?) => "SMALLINT UNSIGNED",
            _ when type == typeof(uint?) => "INT UNSIGNED",
            _ when type == typeof(ulong?) => "BIGINT UNSIGNED",
            _ when type == typeof(float?) => "FLOAT",
            _ when type == typeof(char?) => "CHAR(1)",
            _ when type == typeof(Guid?) => "CHAR(36)",
            _ when type == typeof(TimeSpan?) => "TIME",
            _ => throw new NotSupportedException($"Type '{type.Name}' is not supported for MySQL")
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

            // Create temporary table
            var createTempTableQuery = new StringBuilder();
            createTempTableQuery.AppendLine($"CREATE TEMPORARY TABLE `{tempTableName}` (");

            var columnDefinitions = new List<string>();
            foreach (DataColumn column in data.Columns)
            {
                int? maxStringLength = column.MaxLength > 0 ? column.MaxLength : null;
                string sqlType = GetSqlType(column.DataType, maxStringLength);
                columnDefinitions.Add($"    `{column.ColumnName}` {sqlType}");
            }

            createTempTableQuery.AppendLine(string.Join(",\n", columnDefinitions));
            createTempTableQuery.AppendLine(");");

            using var createTempTableCommand = CreateDbCommand(createTempTableQuery.ToString(), connection);
            await createTempTableCommand.ExecuteNonQueryAsync();

            // Insert data in batches using parameterized queries
            await InsertDataInBatches(data, tempTableName, mySqlConnection, transaction);

            // Perform INSERT ... ON DUPLICATE KEY UPDATE (MySQL's upsert)
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

            // Delete unsynced data if needed
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

        string schemaName = extraction.Origin!.Alias ?? extraction.Origin!.Name;
        string tableName = extraction.Alias ?? extraction.Name;

        try
        {
            using var connection = new MySqlConnection(extraction.Destination!.ConnectionString);
            await connection.OpenAsync();

            using var transaction = await connection.BeginTransactionAsync();

            await InsertDataInBatches(data, $"`{schemaName}`.`{tableName}`", connection, transaction);

            await transaction.CommitAsync();
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
            using var mySqlConnection = (MySqlConnection)connection;
            using var transaction = await mySqlConnection.BeginTransactionAsync();

            await InsertDataInBatches(data, $"`{schemaName}`.`{tableName}`", mySqlConnection, transaction);

            await transaction.CommitAsync();
            Log.Information($"Bulk loaded {data.Rows.Count} rows into {schemaName}.{tableName}");
            return Result.Ok();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Bulk load failed for table {SchemaName}.{TableName}", schemaName, tableName);
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    private async Task InsertDataInBatches(DataTable data, string tableName, MySqlConnection connection, MySqlTransaction transaction)
    {
        const int batchSize = 1000;
        var totalRows = data.Rows.Count;
        
        for (int i = 0; i < totalRows; i += batchSize)
        {
            var batchRows = data.Rows.Cast<DataRow>().Skip(i).Take(batchSize).ToList();
            
            var insertQuery = new StringBuilder();
            insertQuery.AppendLine($"INSERT INTO {tableName} (");
            
            var columnNames = data.Columns.Cast<DataColumn>().Select(c => $"`{c.ColumnName}`");
            insertQuery.AppendLine(string.Join(", ", columnNames));
            insertQuery.AppendLine(") VALUES ");

            var valuesList = new List<string>();
            var paramIndex = 0;
            using var batchCommand = new MySqlCommand("", connection, transaction);

            foreach (var row in batchRows)
            {
                var paramNames = new List<string>();
                for (int colIndex = 0; colIndex < data.Columns.Count; colIndex++)
                {
                    var paramName = $"@p{paramIndex++}";
                    paramNames.Add(paramName);
                    var value = row[colIndex];
                    
                    // Fix: Create parameter properly for MySQL.Data
                    var parameter = new MySqlParameter(paramName, value == DBNull.Value ? null : value);
                    batchCommand.Parameters.Add(parameter);
                }
                valuesList.Add($"({string.Join(", ", paramNames)})");
            }

            insertQuery.Append(string.Join(",\n", valuesList));
            batchCommand.CommandText = insertQuery.ToString();
            batchCommand.CommandTimeout = Settings.QueryTimeout;
            await batchCommand.ExecuteNonQueryAsync();
            
            Log.Debug($"Inserted batch {i / batchSize + 1}: {batchRows.Count} rows");
        }
    }
}