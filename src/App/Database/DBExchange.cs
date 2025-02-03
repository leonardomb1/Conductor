using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Text;
using Conductor.Logging;
using Conductor.Model;
using Conductor.Service;
using Conductor.Shared.Config;
using Conductor.Shared.Types;

namespace Conductor.App.Database;

public abstract class DBExchange
{
    protected abstract string? QueryNonLocking();

    protected abstract string? QueryPagination(UInt64 current);

    protected abstract DbCommand CreateDbCommand(string query, DbConnection connection);

    public abstract DbConnection CreateConnection(string conStr);

    protected abstract string GetSqlType(Type dataType, Int32? lenght);

    public virtual string GetCastType(string column, Type type, Int32? lenght) => $"CAST({column} AS {GetSqlType(type, lenght)})";

    protected abstract StringBuilder AddSurrogateKey(StringBuilder stringBuilder, string index, string tableName, string? virtualIdGroup = null);

    protected abstract StringBuilder AddChangeColumn(StringBuilder stringBuilder, string tableName);

    protected abstract StringBuilder AddIdentityColumn(StringBuilder stringBuilder, string tableName);

    protected abstract StringBuilder AddColumnarStructure(StringBuilder stringBuilder, string tableName);

    protected abstract Task EnsureSchemaCreation(string system, DbConnection connection);

    public abstract Task<Result> BulkLoad(DataTable data, Extraction extraction);

    public abstract Task<Result> BulkLoad(DataTable data, Extraction extraction, DbConnection connection);

    protected abstract string GeneratePartitionCondition(Extraction extraction, double timeZoneOffSet, string? virtualColumn = null);

    protected virtual StringBuilder AddPrimaryKey(StringBuilder stringBuilder, string tableName)
    {
        return stringBuilder.Append($" CONSTRAINT IX_{tableName}_PK PRIMARY KEY (ID_DW_{tableName}),");
    }

    public abstract Task<Result> MergeLoad(DataTable data, Extraction extraction, DbConnection connection);

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

        try
        {
            var res = await command.ExecuteScalarAsync();
            return res != null;
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

        try
        {
            var res = await command.ExecuteScalarAsync();
            return res != null;
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

    public virtual async Task<Result> ClearTable(Extraction extraction, DataTable data, DbConnection connection, UInt64 rowCount)
    {
        if (
            !extraction.IsIncremental ||
            extraction.SingleExecution ||
            rowCount == 0 ||
            data.Rows.Count == 0
        ) return Result.Ok();

        string tableName = extraction.Alias ?? extraction.Name;
        string schemaName = extraction.Origin!.Alias ?? extraction.Origin!.Name;
        string virtualIdGroup = extraction.VirtualIdGroup ?? "file";

        StringBuilder builder = new();

        string columnCondition = extraction.IsVirtual ?
            $"AND {GetCastType($"\"{extraction.IndexName}\"", typeof(string), extraction.IndexName.Length)} + '{Settings.SplitterChar}' + \"{tableName}_{virtualIdGroup}\"" :
            $"AND \"{extraction.IndexName}\"";

        builder.Append($"DELETE FROM \"{schemaName}\".\"{tableName}\" WHERE 1 = 1 {columnCondition} IN (");

        var values = data.Rows.Cast<DataRow>().Select(row =>
            extraction.IsVirtual ? $"'{row[extraction.IndexName]}{Settings.SplitterChar}{row[$"{tableName}_{virtualIdGroup}"]}'" : $"{row[extraction.IndexName]}"
        );

        builder.Append($"{string.Join(",", values)})");

        try
        {
            Log.Out($"Clearing table {schemaName}.{tableName}...");
            using DbCommand command = CreateDbCommand(builder.ToString(), connection);
            await command.ExecuteNonQueryAsync();
            return Result.Ok();
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
            Log.Out($"Droping table {schemaName}.{tableName}...");
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

        try
        {
            Log.Out($"Droping table {schemaName}.{tableName}...");
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

    public virtual async Task<Result<DataTable>> SingleFetch(
        Extraction extraction,
        UInt64 current,
        bool shouldPartition,
        string? virtualizedTable = null,
        string? virtualizedIdGroup = null,
        CancellationToken token = default
    )
    {
        using DbConnection connection = CreateConnection(extraction.Origin!.ConnectionString);

        string orderMode = shouldPartition ? "DESC" : "ASC";

        string partitioning = "";
        if (shouldPartition)
        {
            partitioning = extraction.IsIncremental ? GeneratePartitionCondition(extraction, extraction.Origin!.TimeZoneOffSet) : "";
        }

        string columns;
        if (extraction.VirtualId != null && virtualizedTable != null)
        {
            columns = $"'{extraction.VirtualId}' AS \"{VirtualColumn(virtualizedTable, virtualizedIdGroup!)}\", *";
        }
        else
        {
            columns = "*";
        }

        using DbCommand command = CreateDbCommand(
            @$"SELECT {columns} FROM {extraction.Name}
                {QueryNonLocking()}
                {partitioning}
            ORDER BY {extraction.IndexName} {orderMode}
            {QueryPagination(current)}",
            connection
        );

        try
        {
            await connection.OpenAsync(token);

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
        finally
        {
            await connection.CloseAsync();
        }
    }

    public virtual async Task<Result<DataTable>> FetchDataTable(
        Extraction extraction,
        bool shouldPartition,
        UInt64 current,
        CancellationToken token
    )
    {
        if (extraction.IsVirtual)
        {
            var deps = await ExtractionService.GetDependencies(extraction);
            if (!deps.IsSuccessful) return deps.Error;

            return await ParallelFetch(deps.Value, current, shouldPartition, extraction.Name, extraction.VirtualIdGroup!, token);
        }
        else
        {
            return await SingleFetch(extraction, current, shouldPartition, extraction.Name, extraction.VirtualIdGroup, token);
        }
    }

    public virtual async Task<Result<DataTable>> ParallelFetch(
        List<Extraction> extractions,
        UInt64 current,
        bool shouldPartition,
        string virtualizedTable,
        string virtualIdGroup,
        CancellationToken token
    )
    {
        ConcurrentBag<DataTable> dataTables = [];
        DataTable data = new();
        bool gotTemplate = false;
        byte errCount = 0;

        try
        {
            await Parallel.ForEachAsync(extractions, token, async (e, t) =>
            {
                var fetch = await SingleFetch(e, current, shouldPartition, virtualizedTable, virtualIdGroup, t);
                if (!fetch.IsSuccessful)
                {
                    errCount++;
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
        if (extraction.TableStructure == "Columnar") queryBuilder = AddColumnarStructure(queryBuilder, tableName);
        queryBuilder.AppendLine(");");

        try
        {
            await EnsureSchemaCreation(schemaName, connection);

            Log.Out($"Creating table {schemaName}.{tableName}...");
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
        string schemaName = extraction.Origin!.Alias ?? extraction.Origin!.Name;
        string tableName = extraction.Alias ?? extraction.Name;

        var verify = await Exists(extraction, connection);
        if (!verify.IsSuccessful) return verify.Error;

        if (verify.Value)
        {
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
        if (extraction.TableStructure == "Columnar") queryBuilder = AddColumnarStructure(queryBuilder, tableName);
        queryBuilder.AppendLine(");");

        try
        {
            await EnsureSchemaCreation(schemaName, connection);

            Log.Out($"Creating table {schemaName}.{tableName}...");
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