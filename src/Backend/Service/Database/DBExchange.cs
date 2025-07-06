using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Text;
using Conductor.Model;
using Conductor.Repository;
using Conductor.Shared;
using Conductor.Types;
using Serilog;

namespace Conductor.Service.Database;

public abstract class DBExchange
{
    private static readonly HashSet<string> MARSCompatibleDatabases = ["SqlServer"];

    public static bool SupportsMARS(string dbType) => MARSCompatibleDatabases.Contains(dbType);

    protected abstract string? QueryNonLocking();

    protected abstract string? QueryPagination(UInt64 current);

    protected abstract DbCommand CreateDbCommand(string query, DbConnection connection);

    public abstract DbConnection CreateConnection(string conStr);

    protected abstract string GetSqlType(Type dataType, Int32? lenght);

    public virtual string GetCastType(string column, Type type, Int32? lenght) => $"CAST({column} AS {GetSqlType(type, lenght)})";

    protected abstract StringBuilder AddSurrogateKey(StringBuilder stringBuilder, string index, string tableName, string? virtualIdGroup = null);

    protected abstract StringBuilder AddChangeColumn(StringBuilder stringBuilder, string tableName);

    protected abstract StringBuilder AddIdentityColumn(StringBuilder stringBuilder, string tableName);

    protected abstract Task EnsureSchemaCreation(string system, DbConnection connection);

    public abstract Task<Result> BulkLoad(DataTable data, Extraction extraction);

    public abstract Task<Result> BulkLoad(DataTable data, Extraction extraction, DbConnection connection);

    protected abstract string GeneratePartitionCondition(Extraction extraction, DateTime requestTime, string? virtualColumn = null);

    protected virtual StringBuilder AddPrimaryKey(StringBuilder stringBuilder, string tableName)
    {
        return stringBuilder.Append($" CONSTRAINT IX_{tableName}_PK PRIMARY KEY (ID_DW_{tableName}),");
    }

    public abstract Task<Result> MergeLoad(DataTable data, Extraction extraction, DateTime requestTime, DbConnection connection);

    protected virtual DateTimeOffset RequestTimeWithOffSet(DateTimeOffset requestTime, Int32 filterTime, Int32 offSet)
    {
        return requestTime.AddSeconds(-filterTime).ToOffset(new TimeSpan(offSet, 0, 0));
    }

    protected virtual string VirtualColumn(string tableName, string fileGroup)
    {
        return $"{tableName}_{fileGroup}";
    }

    public virtual async Task<Result<bool>> Exists(Extraction extraction)
    {
        using DbConnection connection = CreateConnection(extraction.Destination!.ConnectionString);
        await connection.OpenAsync();

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
        finally
        {
            await connection.CloseAsync();
        }
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

    public virtual async Task<Result<UInt64>> CountTableRows(Extraction extraction)
    {
        using DbConnection connection = CreateConnection(extraction.Destination!.ConnectionString);
        await connection.OpenAsync();

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
        finally
        {
            await connection.CloseAsync();
        }
    }

    public virtual async Task<Result<UInt64>> CountTableRows(Extraction extraction, DbConnection connection)
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

    public virtual async Task<Result> DropTable(Extraction extraction)
    {
        string schemaName = extraction.Origin!.Alias ?? extraction.Origin!.Name;
        string tableName = extraction.Alias ?? extraction.Name;

        using DbConnection connection = CreateConnection(extraction.Destination!.ConnectionString);
        await connection.OpenAsync();

        using DbCommand command = CreateDbCommand(
            $"DROP TABLE \"{schemaName}\".\"{tableName}\"",
            connection
        );

        try
        {
            Log.Information($"Droping table {schemaName}.{tableName}...");
            await command.ExecuteNonQueryAsync();
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    public virtual async Task<Result> TruncateTable(Extraction extraction)
    {
        string schemaName = extraction.Origin!.Alias ?? extraction.Origin!.Name;
        string tableName = extraction.Alias ?? extraction.Name;

        using DbConnection connection = CreateConnection(extraction.Destination!.ConnectionString);
        await connection.OpenAsync();

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
        finally
        {
            await connection.CloseAsync();
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

    public virtual async Task<Result<DataTable>> SingleFetch(
        Extraction extraction,
        UInt64 current,
        DateTime requestTime,
        bool shouldPartition,
        Int32? overrideFilter,
        string? virtualizedTable = null,
        string? virtualizedIdGroup = null,
        bool shouldPaginate = true,
        CancellationToken token = default
    )
    {
        if (token.IsCancellationRequested) return new Error("Operation Cancelled.");

        using DbConnection connection = CreateConnection(extraction.Origin!.ConnectionString!);
        await connection.OpenAsync(token);

        string metadata = "*";

        extraction.FilterTime = overrideFilter ?? extraction.FilterTime;

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
            GeneratePartitionCondition(extraction, requestTime) : "";

        string condition = $"{extraction.FilterCondition}" ?? "";

        string queryBase = extraction.OverrideQuery ??
            @$"SELECT {columns} FROM {extraction.Name} {QueryNonLocking()}
            WHERE 1 = 1 {condition} {partitioning}
            ORDER BY {extraction.IndexName} {(shouldPartition ? "DESC" : "ASC")}";

        string query = $"{queryBase} {(shouldPaginate ? QueryPagination(current) : "")}";

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
        finally
        {
            await connection.CloseAsync();
        }
    }

    public virtual async Task<Result<DataTable>> SingleFetch(
        Extraction extraction,
        UInt64 current,
        DateTime requestTime,
        bool shouldPartition,
        DbConnection connection,
        Int32? overrideFilter,
        string? virtualizedTable = null,
        string? virtualizedIdGroup = null,
        bool shouldPaginate = true,
        CancellationToken token = default
    )
    {
        if (token.IsCancellationRequested) return new Error("Operation Cancelled.");

        string metadata = "*";

        extraction.FilterTime = overrideFilter ?? extraction.FilterTime;

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
            GeneratePartitionCondition(extraction, requestTime) : "";

        string condition = $"{extraction.FilterCondition}" ?? "";

        string queryBase = extraction.OverrideQuery ??
            @$"SELECT {columns} FROM {extraction.Name} {QueryNonLocking()}
            WHERE 1 = 1 {condition} {partitioning}
            ORDER BY {extraction.IndexName} {(shouldPartition ? "DESC" : "ASC")}";

        string query = $"{queryBase} {(shouldPaginate ? QueryPagination(current) : "")}";

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
        UInt64 current,
        CancellationToken token,
        Int32? overrideFilter = null,
        bool shouldPaginate = true
    )
    {
        if (token.IsCancellationRequested) return new Error("Operation Cancelled.");

        if (extraction.IsVirtual)
        {
            var deps = await ExtractionRepository.GetDependencies(extraction);
            if (!deps.IsSuccessful) return deps.Error;

            return await ParallelFetch(
                deps.Value,
                current,
                requestTime,
                shouldPartition,
                overrideFilter,
                extraction.Name,
                extraction.VirtualIdGroup!,
                token,
                shouldPaginate
            );
        }
        else
        {
            return await SingleFetch(
                extraction,
                current,
                requestTime,
                shouldPartition,
                overrideFilter,
                extraction.Name,
                extraction.VirtualIdGroup,
                shouldPaginate,
                token
            );
        }
    }

    public virtual async Task<Result<DataTable>> FetchDataTable(
        Extraction extraction,
        DateTime requestTime,
        bool shouldPartition,
        UInt64 current,
        DbConnection connection,
        CancellationToken token,
        Int32? overrideFilter = null,
        bool shouldPaginate = true
    )
    {
        if (extraction.IsVirtual)
        {
            var deps = await ExtractionRepository.GetDependencies(extraction);
            if (!deps.IsSuccessful) return deps.Error;

            return await ParallelFetch(
                    deps.Value,
                    current,
                    requestTime,
                    shouldPartition,
                    overrideFilter,
                    extraction.Name,
                    extraction.VirtualIdGroup!,
                    connection,
                    token,
                    shouldPaginate
                );
        }
        else
        {
            return await SingleFetch(
                extraction,
                current,
                requestTime,
                shouldPartition,
                connection,
                overrideFilter,
                extraction.Name,
                extraction.VirtualIdGroup,
                shouldPaginate,
                token
            );
        }
    }

    public virtual async Task<Result<DataTable>> ParallelFetch(
        List<Extraction> extractions,
        UInt64 current,
        DateTime requestTime,
        bool shouldPartition,
        Int32? overrideTime,
        string virtualizedTable,
        string virtualIdGroup,
        CancellationToken token,
        bool shouldPaginate = true
    )
    {
        ConcurrentBag<DataTable> dataTables = [];
        DataTable data = new();
        bool gotTemplate = false;
        Int32 errCount = 0;

        try
        {
            if (token.IsCancellationRequested) return new Error("Operation Cancelled.");
            await Parallel.ForEachAsync(extractions, token, async (e, t) =>
            {
                if (t.IsCancellationRequested) return;
                var fetch = await SingleFetch(
                        e,
                        current,
                        requestTime,
                        shouldPartition,
                        overrideTime,
                        virtualizedTable,
                        virtualIdGroup,
                        shouldPaginate,
                        t
                );
                if (!fetch.IsSuccessful)
                {
                    Interlocked.Increment(ref errCount);
                    return;
                }

                bool isTemplate = e.IsVirtualTemplate ?? false;

                for (Int32 i = 0; i < fetch.Value.Columns.Count; i++)
                {
                    fetch.Value.Columns[i].AllowDBNull = true;
                }

                if (isTemplate)
                {
                    data = fetch.Value.Clone();
                    gotTemplate = true;
                }

                dataTables.Add(fetch.Value);
            });

            if (errCount == extractions.Count) return new Error("Failed to fetch data from all tables.");
            if (!gotTemplate) data = dataTables.First().Clone();

            foreach (DataTable table in dataTables)
            {
                data.Merge(table, false, MissingSchemaAction.Ignore);
                table.Dispose();
            }

            data.TableName = virtualizedTable;

            return data;
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
        finally
        {
            dataTables.Clear();
        }
    }

    public virtual async Task<Result<DataTable>> ParallelFetch(
        List<Extraction> extractions,
        UInt64 current,
        DateTime requestTime,
        bool shouldPartition,
        Int32? overrideTime,
        string virtualizedTable,
        string virtualIdGroup,
        DbConnection connection,
        CancellationToken token,
        bool shouldPaginate = true
    )
    {
        ConcurrentBag<DataTable> dataTables = [];
        DataTable data = new();
        bool gotTemplate = false;
        Int32 errCount = 0;

        try
        {
            if (token.IsCancellationRequested) return new Error("Operation Cancelled.");

            await Parallel.ForEachAsync(extractions, token, async (e, t) =>
            {
                if (t.IsCancellationRequested) return;
                var fetch = await SingleFetch(
                    e,
                    current,
                    requestTime,
                    shouldPartition,
                    connection,
                    overrideTime,
                    virtualizedTable,
                    virtualIdGroup,
                    shouldPaginate,
                    t
                );
                if (!fetch.IsSuccessful)
                {
                    Interlocked.Increment(ref errCount);
                    return;
                }

                bool isTemplate = e.IsVirtualTemplate ?? false;

                for (Int32 i = 0; i < fetch.Value.Columns.Count; i++)
                {
                    fetch.Value.Columns[i].AllowDBNull = true;
                }

                if (isTemplate)
                {
                    data = fetch.Value.Clone();
                    gotTemplate = true;
                }

                dataTables.Add(fetch.Value);
            });

            if (errCount == extractions.Count) return new Error("Failed to fetch data from all tables.");
            if (!gotTemplate) data = dataTables.First().Clone();

            foreach (DataTable table in dataTables)
            {
                data.Merge(table, false, MissingSchemaAction.Ignore);
                table.Dispose();
            }

            data.TableName = virtualizedTable;

            return data;
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
        finally
        {
            dataTables.Clear();
        }
    }

    public virtual async Task<Result> CreateTable(DataTable table, Extraction extraction)
    {
        if (extraction.IndexName is null) return new Error("Invalid metadata, missing index name.");
        string schemaName = extraction.Origin!.Alias ?? extraction.Origin!.Name;
        string tableName = extraction.Alias ?? extraction.Name;

        using var connection = CreateConnection(extraction.Destination!.ConnectionString);
        await connection.OpenAsync();

        var verify = await Exists(extraction, connection);
        if (!verify.IsSuccessful) return verify.Error;

        if (verify.Value)
        {
            await connection.CloseAsync();
            return Result.Ok();
        }

        var queryBuilder = new StringBuilder();

        queryBuilder.AppendLine($"CREATE TABLE \"{schemaName}\".\"{tableName}\" (");

        foreach (DataColumn column in table.Columns)
        {
            Int32? maxStringLength = column.MaxLength;
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
        finally
        {
            await connection.CloseAsync();
        }
    }

    public virtual async Task<Result> CreateTable(DataTable table, Extraction extraction, DbConnection connection)
    {
        if (extraction.IndexName is null) return new Error("Invalid metadata, missing index name.");
        string schemaName = extraction.Origin!.Alias ?? extraction.Origin!.Name;
        string tableName = extraction.Alias ?? extraction.Name;

        var queryBuilder = new StringBuilder();

        queryBuilder.AppendLine($"CREATE TABLE \"{schemaName}\".\"{tableName}\" (");

        foreach (DataColumn column in table.Columns)
        {
            Int32? maxStringLength = column.MaxLength;
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
}