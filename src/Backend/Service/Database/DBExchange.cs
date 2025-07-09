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

    protected abstract string GeneratePartitionCondition(Extraction extraction, DateTime requestTime, string? virtualColumn = null);

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
            if (extraction.VirtualIdGroup is null) return new Error("No Virtual Id Group was given.");

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

        ConcurrentBag<DataTable> dataTables = [];
        ConcurrentBag<Error> errors = [];
        DataTable data = new();
        bool gotTemplate = false;

        try
        {
            await Parallel.ForEachAsync(extractions, token, async (extraction, token) =>
            {
                if (token.IsCancellationRequested) return;
                bool isTemplate = extraction.IsVirtualTemplate ?? false;

                Result<DataTable> fetch = await SelectData(
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

                if (!fetch.IsSuccessful)
                {
                    errors.Add(fetch.Error);
                    return;
                }

                for (int i = 0; i < fetch.Value.Columns.Count; i++)
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

            if (!errors.IsEmpty) return Result<DataTable>.Err([.. errors]);
            if (gotTemplate)
            {
                foreach (DataTable table in dataTables)
                {
                    data.Merge(table, false, MissingSchemaAction.Ignore);
                    table.Dispose();
                }

                data.TableName = virtualizedTable;

                return data;
            }
            else
            {
                return new Error("No template was given.");
            }
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

    public virtual async Task<Result> CreateTable(DataTable table, Extraction extraction, DbConnection connection)
    {
        if (extraction.Origin is null) return new Error("No origin was given.");
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
}