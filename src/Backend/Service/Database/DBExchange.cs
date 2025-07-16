using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static async Task<DbConnection> GetConnectionAsync(string connectionString, string dbType, IConnectionPoolManager connectionPoolManager, CancellationToken cancellationToken = default)
    {
        string finalConnectionString = SupportsMARS(dbType)
            ? EnsureMARSEnabled(connectionString, dbType)
            : connectionString;

        return await connectionPoolManager.GetConnectionAsync(finalConnectionString, dbType, cancellationToken);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ReturnConnection(string connectionString, string dbType, IConnectionPoolManager connectionPoolManager, DbConnection connection)
    {
        if (connection is null) return;

        string finalConnectionString = SupportsMARS(dbType)
            ? EnsureMARSEnabled(connectionString, dbType)
            : connectionString;

        string connectionKey = GenerateConnectionKey(finalConnectionString, dbType);
        connectionPoolManager.ReturnConnection(connectionKey, connection);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string GenerateConnectionKey(string connectionString, string dbType)
    {
        return string.Concat(dbType.AsSpan(), ":".AsSpan(), connectionString.GetHashCode().ToString().AsSpan());
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
            using var select = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, token);

            fetched.BeginLoadData();
            fetched.Load(select);
            fetched.EndLoadData();

            fetched.TableName = extraction.Alias ?? extraction.Name;

            Log.Debug("Query returned {RowCount} rows for extraction {ExtractionId} in {QueryLength} chars", 
                fetched.Rows.Count, extraction.Id, query.Length);
            return fetched;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Optimized query execution failed for extraction {ExtractionId}: {Query}", extraction.Id, query);
            return new Error(ex.Message, ex.StackTrace);
        }
    }

    public virtual async Task<Result<DataTable>> FetchDataTable(
        Extraction extraction,
        DateTime requestTime,
        bool shouldPartition,
        ulong currentRowCount,
        IConnectionPoolManager connectionPoolManager,
        CancellationToken token,
        int? overrideFilter = null,
        ulong limit = 0,
        bool shouldPaginate = true
    )
    {
        if (token.IsCancellationRequested) return new Error("Operation Cancelled.");

        if (extraction.IsVirtual)
        {
            if (extraction.VirtualIdGroup is null) 
                return new Error($"No Virtual Id Group was given for {extraction.Name}, id: {extraction.Id}");

            Result<List<Extraction>> deps = await ExtractionRepository.GetDependencies(extraction);
            if (!deps.IsSuccessful) return deps.Error;

            return await ParallelSelect(
                    deps.Value,
                    connectionPoolManager,
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
            DbConnection? pooledConnection = null;
            try
            {
                pooledConnection = await GetConnectionAsync(
                    extraction.Origin!.ConnectionString!,
                    extraction.Origin!.DbType!,
                    connectionPoolManager,
                    token
                );

                var result = await SelectData(
                    extraction,
                    currentRowCount,
                    requestTime,
                    shouldPartition,
                    pooledConnection,
                    overrideFilter, 
                    extraction.Name,
                    extraction.VirtualIdGroup,
                    shouldPaginate,
                    limit,
                    token
                );

                return result;
            }
            finally
            {
                if (pooledConnection is not null)
                {
                    ReturnConnection(
                        extraction.Origin!.ConnectionString!,
                        extraction.Origin!.DbType!,
                        connectionPoolManager,
                        pooledConnection
                    );
                }
            }
        }
    }
    
    public virtual async Task<Result<DataTable>> ParallelSelect(
        List<Extraction> extractions,
        IConnectionPoolManager connectionPoolManager,
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

        var dataTables = new ConcurrentBag<DataTable>();
        DataTable? finalData = new();
        bool gotTemplate = false;
        int errCount = 0;
        Lock templateLock = new();

        try
        {
            await Parallel.ForEachAsync(extractions, token, async (extraction, cancellationToken) =>
            {
                if (cancellationToken.IsCancellationRequested) return;

                DbConnection? pooledConnection = null;
                try
                {
                    pooledConnection = await connectionPoolManager.GetConnectionAsync(
                        extraction.Origin!.ConnectionString!,
                        extraction.Origin!.DbType!,
                        cancellationToken
                    );

                    Log.Debug("Retrieved connection from pool for extraction {ExtractionId} ({ExtractionName})",
                        extraction.Id, extraction.Name);

                    var result = await SelectData(
                        extraction,
                        currentRowCount,
                        requestTime,
                        shouldPartition,
                        pooledConnection,
                        overrideTime,
                        virtualizedTable,
                        virtualIdGroup,
                        shouldPaginate,
                        limit,
                        cancellationToken
                    );

                    if (!result.IsSuccessful)
                    {
                        Interlocked.Increment(ref errCount);
                        Log.Error("Failed to fetch data for extraction {ExtractionId} ({ExtractionName}): {Error}",
                            extraction.Id, extraction.Name, result.Error.ExceptionMessage);
                        return;
                    }

                    var fetchResult = result.Value;
                    bool isTemplate = extraction.IsVirtualTemplate ?? false;

                    for (int i = 0; i < fetchResult.Columns.Count; i++)
                    {
                        fetchResult.Columns[i].AllowDBNull = true;
                    }

                    if (isTemplate && !gotTemplate)
                    {
                        lock (templateLock)
                        {
                            if (!gotTemplate)
                            {
                                finalData = fetchResult.Clone();
                                gotTemplate = true;
                                Log.Information("Set template table structure from extraction {ExtractionId} with {RowCount} rows",
                                    extraction.Id, fetchResult.Rows.Count);
                            }
                        }
                    }

                    dataTables.Add(fetchResult);
                    Log.Information("Successfully fetched {RowCount} rows from extraction {ExtractionId} using pooled connection",
                        fetchResult.Rows.Count, extraction.Id);
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref errCount);
                    Log.Error(ex, "Error fetching data for extraction {ExtractionId}: {Message}",
                        extraction.Id, ex.Message);
                }
                finally
                {
                    if (pooledConnection is not null)
                    {
                        try
                        {
                            var connectionKey = $"{extraction.Origin!.DbType}:{extraction.Origin!.ConnectionString!.GetHashCode()}";
                            connectionPoolManager.ReturnConnection(connectionKey, pooledConnection);
                            Log.Debug("Returned connection to pool for extraction {ExtractionId}", extraction.Id);
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "Failed to return connection to pool for extraction {ExtractionId}", extraction.Id);
                            try { pooledConnection.Dispose(); } catch { }
                        }
                    }
                }
            });
            
            if (errCount == extractions.Count)
            {
                return new Error("Failed to fetch data from all extractions in virtual table");
            }

            if (!gotTemplate && !dataTables.IsEmpty)
            {
                finalData = dataTables.First().Clone();
                Log.Warning("No template found for virtual table {VirtualizedTable}, using first extraction as template", virtualizedTable);
            }

            if (finalData == null)
            {
                return new Error($"No template could be established for virtual table {virtualizedTable}");
            }

            var totalExpectedRows = dataTables.Sum(t => t.Rows.Count);
            Log.Information("Starting merge for virtual table {VirtualizedTable}: {ExtractionCount} extractions, {ExpectedRows} total rows",
                virtualizedTable, extractions.Count, totalExpectedRows);

            var mergeNumber = 0;
            foreach (DataTable table in dataTables)
            {
                mergeNumber++;
                try
                {
                    var rowsBeforeMerge = finalData.Rows.Count;
                    finalData.Merge(table, false, MissingSchemaAction.Ignore);
                    var rowsAfterMerge = finalData.Rows.Count;
                    var actualRowsAdded = rowsAfterMerge - rowsBeforeMerge;

                    Log.Information("Merge {MergeNumber}: Added {ActualRows} of {ExpectedRows} rows from table {TableName}, total now: {TotalRows}",
                        mergeNumber, actualRowsAdded, table.Rows.Count, table.TableName, rowsAfterMerge);

                    if (actualRowsAdded != table.Rows.Count)
                    {
                        var lostRows = table.Rows.Count - actualRowsAdded;
                        Log.Warning("Merge {MergeNumber}: Lost {LostRows} rows during merge (possible key conflicts)",
                            mergeNumber, lostRows);
                    }

                    table.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error merging table {TableName} with {RowCount} rows in merge {MergeNumber}",
                        table.TableName, table.Rows.Count, mergeNumber);
                }
            }

            finalData.TableName = virtualizedTable;
            var finalRowCount = finalData.Rows.Count;

            Log.Information("ParallelSelect completed for {VirtualizedTable}: {FinalRows} final rows (expected {ExpectedRows})",
                virtualizedTable, finalRowCount, totalExpectedRows);

            if (finalRowCount != totalExpectedRows)
            {
                var dataLoss = totalExpectedRows - finalRowCount;
                Log.Warning("Data loss detected in virtual table {VirtualizedTable}: {DataLoss} rows lost during merge",
                    virtualizedTable, dataLoss);
            }

            return finalData;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected error in ParallelSelect for virtual table {VirtualizedTable}", virtualizedTable);
            return new Error(ex.Message, ex.StackTrace);
        }
        finally
        {
            dataTables.Clear();
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