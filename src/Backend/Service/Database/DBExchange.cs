using System.Data;
using System.Data.Common;
using System.Text;
using System.Text.RegularExpressions;
using Conductor.Model;
using Conductor.Repository;
using Conductor.Shared;
using Conductor.Types;
using Serilog;

namespace Conductor.Service.Database;

public abstract partial class DBExchange
{
    private static readonly HashSet<string> MARSCompatibleDatabases = ["SqlServer"];

    public static bool SupportsMARS(string dbType) => MARSCompatibleDatabases.Contains(dbType);

    public static string EnsureMARSEnabled(string connectionString, string dbType)
    {
        if (!SupportsMARS(dbType))
            return connectionString;

        if (connectionString.Contains("MultipleActiveResultSets", StringComparison.OrdinalIgnoreCase))
        {
            return MyRegex().Replace(connectionString, "MultipleActiveResultSets=true");
        }

        string separator = connectionString.EndsWith(';') ? "" : ";";
        return $"{connectionString}{separator}MultipleActiveResultSets=true";
    }

    protected abstract string? QueryNonLocking();

    protected abstract string? QueryPagination(ulong rows, ulong limit);

    protected abstract DbCommand CreateDbCommand(string query, DbConnection connection);

    public abstract DbConnection CreateConnection(string conStr);

    protected abstract string GetSqlType(Type dataType, int? lenght);

    protected abstract StringBuilder AddSurrogateKey(StringBuilder stringBuilder, string index, string tableName, string? virtualIdGroup = null);

    protected abstract StringBuilder AddChangeColumn(StringBuilder stringBuilder, string tableName);

    protected abstract StringBuilder AddIdentityColumn(StringBuilder stringBuilder, string tableName);

    protected abstract Task EnsureSchemaCreation(string system, DbConnection connection);

    public abstract Task<Result> BulkLoad(DataTable data, Extraction extraction);

    public abstract Task<Result> BulkLoad(DataTable data, Extraction extraction, DbConnection connection);

    protected abstract string GeneratePartitionCondition(Extraction extraction, DateTime requestTime, string? virtualColumn = null, int? effectiveFilterTime = null);

    protected virtual StringBuilder AddPrimaryKey(StringBuilder stringBuilder, string tableName)
    {
        return stringBuilder.Append($" CONSTRAINT IX_{tableName}_PK PRIMARY KEY (ID_DW_{tableName}),");
    }
    public abstract Task<Result> MergeLoad(DataTable data, Extraction extraction, DateTime requestTime, DbConnection connection);

    protected virtual DateTimeOffset RequestTimeWithOffSet(DateTimeOffset requestTime, int filterTime, int offSet)
    {
        return requestTime.AddSeconds(-filterTime).ToOffset(new TimeSpan(offSet, 0, 0));
    }

    protected virtual string VirtualColumn(string tableName, string fileGroup)
    {
        return $"{tableName}_{fileGroup}";
    }

    public virtual async Task<Result<bool>> Exists(Extraction extraction, DbConnection connection)
    {
        string schemaName = extraction.Origin!.Alias ?? extraction.Origin!.Name;
        string tableName = extraction.Alias ?? extraction.Name;

        using DbCommand command = CreateDbCommand(
            @$"
                SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES {QueryNonLocking()} 
                WHERE TABLE_NAME = '{tableName}' AND TABLE_SCHEMA = '{schemaName}'",
            connection
        );

        command.CommandTimeout = Settings.QueryTimeout;

        try
        {
            var res = await command.ExecuteScalarAsync();
            return res is not null;
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    public virtual async Task<Result<ulong>> CountTableRows(Extraction extraction, DbConnection connection)
    {
        string schemaName = extraction.Origin!.Alias ?? extraction.Origin!.Name;
        string tableName = extraction.Alias ?? extraction.Name;

        using DbCommand command = CreateDbCommand(
            $"SELECT COUNT(*) FROM  \"{schemaName}\".\"{tableName}\" {QueryNonLocking()}",
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

    public virtual async Task<Result> TruncateTable(Extraction extraction, DbConnection connection)
    {
        string schemaName = extraction.Origin!.Alias ?? extraction.Origin!.Name;
        string tableName = extraction.Alias ?? extraction.Name;

        using DbCommand command = CreateDbCommand(
            $"TRUNCATE TABLE \"{schemaName}\".\"{tableName}\"",
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

    protected virtual async Task<Result<List<string>>> GetColumnInformation(DbConnection connection, string tableName, CancellationToken token)
    {
        if (token.IsCancellationRequested) return new Error("Operation Cancelled.");

        using DbCommand command = CreateDbCommand(
            $"SELECT \"COLUMN_NAME\" FROM \"INFORMATION_SCHEMA\".\"COLUMNS\" WHERE \"TABLE_NAME\" = '{tableName}'",
            connection
        );
        command.CommandTimeout = Settings.QueryTimeout;

        List<string> columns = [];

        try
        {
            using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                columns.Add(reader.GetFieldValue<string>("COLUMN_NAME"));
            }

            return columns;
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    public virtual async Task<Result<DataTable>> SelectData(
        Extraction extraction,
        ulong currentRowCount,
        DateTime requestTime,
        bool shouldPartition,
        DbConnection connection,
        int? overrideFilter,
        string? virtualizedTable = null,
        string? virtualizedIdGroup = null,
        bool shouldPaginate = true,
        ulong limit = 0,
        CancellationToken token = default
    )
    {
        if (token.IsCancellationRequested) return new Error("Operation Cancelled.");

        string metadata = "*";

        int? effectiveFilterTime = overrideFilter ?? extraction.FilterTime;

        if (extraction.IgnoreColumns is not null)
        {
            var lookUpColumns = await GetColumnInformation(connection, extraction.Name, token);
            if (!lookUpColumns.IsSuccessful) return lookUpColumns.Error;

            List<string> list = lookUpColumns.Value;
            list = [.. list.Where(
                col => !extraction.IgnoreColumns.Split(Settings.SplitterChar).Any(ig => ig == col)
            )];

            metadata = string.Join(",", list.Select(s => $"\"{s}\""));
        }

        string columns = extraction.VirtualId is not null && virtualizedTable is not null ?
            $"'{extraction.VirtualId}' AS \"{VirtualColumn(virtualizedTable, virtualizedIdGroup!)}\"" +
            $", {metadata}" : $"{metadata}";

        string partitioning = extraction.IsIncremental && shouldPartition ?
            GeneratePartitionCondition(extraction, requestTime, null, effectiveFilterTime) : "";

        string condition = extraction.FilterCondition ?? "";

        string queryBase = extraction.OverrideQuery ??
            @$"SELECT {columns} FROM {extraction.Name} {QueryNonLocking()}
            WHERE 1 = 1 {condition} {partitioning}
            ORDER BY {extraction.IndexName} {(shouldPartition ? "DESC" : "ASC")}";

        string query = $"{queryBase} {(shouldPaginate ? QueryPagination(currentRowCount, limit) : "")}";

        using DbCommand command = CreateDbCommand(query, connection);
        command.CommandTimeout = Settings.QueryTimeout;

        try
        {
            using var fetched = new DataTable();
            var select = await command.ExecuteReaderAsync(token);
            fetched.Load(select);

            fetched.TableName = extraction.Alias ?? extraction.Name;

            return fetched;
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    public virtual async Task<Result<DataTable>> FetchDataTable(
        Extraction extraction,
        DateTime requestTime,
        bool shouldPartition,
        ulong currentRowCount,
        DbConnection connection,
        CancellationToken token,
        int? overrideFilter = null,
        ulong limit = 0,
        bool shouldPaginate = true
    )
    {
        if (token.IsCancellationRequested) return new Error("Operation Cancelled.");

        if (extraction.IsVirtual)
        {
            if (extraction.VirtualIdGroup is null) return new Error($"No Virtual Id Group was given for {extraction.Name}, id: {extraction.Id}");

            Result<List<Extraction>> deps = await ExtractionRepository.GetDependencies(extraction);
            if (!deps.IsSuccessful) return deps.Error;

            return await ParallelSelect(
                    deps.Value,
                    connection,
                    currentRowCount,
                    requestTime,
                    shouldPartition,
                    overrideFilter, 
                    extraction.Name,
                    extraction.VirtualIdGroup!,
                    token,
                    limit,
                    shouldPaginate
                );
        }
        else
        {
            return await SelectData(
                extraction,
                currentRowCount,
                requestTime,
                shouldPartition,
                connection,
                overrideFilter, 
                extraction.Name,
                extraction.VirtualIdGroup,
                shouldPaginate,
                limit,
                token
            );
        }
    }

    public virtual async Task<Result<DataTable>> ParallelSelect(
        List<Extraction> extractions,
        DbConnection connection,
        ulong currentRowCount,
        DateTime requestTime,
        bool shouldPartition,
        int? overrideTime,
        string virtualizedTable,
        string virtualIdGroup,
        CancellationToken token,
        ulong limit = 0,
        bool shouldPaginate = true
    )
    {
        if (token.IsCancellationRequested) return new Error("Operation Cancelled.");

        var dataTables = new List<DataTable>();
        var errors = new List<Error>();
        DataTable finalData = new();
        DataTable? templateTable = null;
        bool hasTemplate = false;

        try
        {
            foreach (var extraction in extractions)
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }

                bool isTemplate = extraction.IsVirtualTemplate ?? false;

                var fetchResult = await SelectData(
                    extraction,
                    currentRowCount,
                    requestTime,
                    shouldPartition,
                    connection,
                    overrideTime,
                    virtualizedTable,
                    virtualIdGroup,
                    shouldPaginate,
                    limit,
                    token
                );

                if (!fetchResult.IsSuccessful)
                {
                    errors.Add(fetchResult.Error);
                    Log.Error("Failed to fetch data for extraction {ExtractionId} ({ExtractionName}): {Error}", 
                        extraction.Id, extraction.Name, fetchResult.Error.ExceptionMessage);
                    continue; 
                }

                foreach (DataColumn column in fetchResult.Value.Columns)
                {
                    column.AllowDBNull = true;
                }

                if (isTemplate && !hasTemplate)
                {
                    templateTable = fetchResult.Value.Clone();
                    finalData = templateTable;
                    hasTemplate = true;
                    Log.Debug("Set template table from extraction {ExtractionId}", extraction.Id);
                }

                dataTables.Add(fetchResult.Value);
                Log.Debug("Successfully fetched {RowCount} rows from extraction {ExtractionId}", 
                    fetchResult.Value.Rows.Count, extraction.Id);
            }

            if (dataTables.Count == 0)
            {
                return new Error("No data was fetched from any extractions");
            }

            if (!hasTemplate)
            {
                return new Error($"No template was found for virtual table {virtualizedTable}. Available extractions: {string.Join(", ", extractions.Select(e => $"{e.Name} (Template: {e.IsVirtualTemplate})"))}");
            }

            var totalRowsBeforeMerge = finalData.Rows.Count;
            foreach (var table in dataTables)
            {
                try
                {
                    var rowsBeforeMerge = finalData.Rows.Count;
                    finalData.Merge(table, false, MissingSchemaAction.Ignore);
                    var rowsAfterMerge = finalData.Rows.Count;
                    
                    Log.Debug("Merged {TableRows} rows from table {TableName}, total rows now: {TotalRows}", 
                        table.Rows.Count, table.TableName, rowsAfterMerge);
                    
                    if (rowsAfterMerge - rowsBeforeMerge != table.Rows.Count)
                    {
                        Log.Warning("Row count mismatch during merge: expected {Expected}, actual {Actual}", 
                            table.Rows.Count, rowsAfterMerge - rowsBeforeMerge);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error merging table {TableName} with {RowCount} rows", 
                        table.TableName, table.Rows.Count);
                    errors.Add(new Error($"Merge failed for table {table.TableName}: {ex.Message}", ex.StackTrace));
                }
            }

            finalData.TableName = virtualizedTable;
            
            var totalRowsAfterMerge = finalData.Rows.Count;
            Log.Information("ParallelSelect completed for {VirtualizedTable}: {ExtractionCount} extractions, {TotalRows} final rows", 
                virtualizedTable, extractions.Count, totalRowsAfterMerge);

            if (errors.Count > 0)
            {
                Log.Warning("ParallelSelect completed with {ErrorCount} errors but has {RowCount} rows", 
                    errors.Count, finalData.Rows.Count);
                
                if (finalData.Rows.Count == 0)
                {
                    return Result<DataTable>.Err(errors);
                }
            }

            return finalData;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected error in ParallelSelect for {VirtualizedTable}", virtualizedTable);
            return new Error(ex.Message, ex.StackTrace);
        }
        finally
        {
            foreach (var table in dataTables)
            {
                if (table != finalData && table != templateTable)
                {
                    try
                    {
                        table.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Error disposing temporary table {TableName}", table.TableName);
                    }
                }
            }
        }
    }

    public virtual async Task<Result> CreateTable(DataTable table, Extraction extraction, DbConnection connection)
    {
        if (extraction.Origin is null) return new Error($"No origin was given for {extraction.Name}, id: {extraction.Id}");
        if (extraction.IndexName is null) return new Error("Invalid metadata, missing index name.");
        string schemaName = extraction.Origin.Alias ?? extraction.Origin.Name;
        string tableName = extraction.Alias ?? extraction.Name;

        var queryBuilder = new StringBuilder();

        queryBuilder.AppendLine($"CREATE TABLE \"{schemaName}\".\"{tableName}\" (");

        foreach (DataColumn column in table.Columns)
        {
            int? maxStringLength = column.MaxLength;
            string SqlType = GetSqlType(column.DataType, maxStringLength);
            queryBuilder.AppendLine($"    \"{column.ColumnName}\" {SqlType},");
        }
        queryBuilder = AddChangeColumn(queryBuilder, tableName);
        queryBuilder = AddIdentityColumn(queryBuilder, tableName);
        queryBuilder = AddPrimaryKey(queryBuilder, tableName);
        queryBuilder = AddSurrogateKey(queryBuilder, extraction.IndexName, tableName, extraction.VirtualIdGroup);
        queryBuilder.AppendLine(");");

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

    [GeneratedRegex(@"MultipleActiveResultSets\s*=\s*[^;]*", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex MyRegex();
}