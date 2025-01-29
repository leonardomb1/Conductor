using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Text;
using Conductor.Logging;
using Conductor.Model;
using Conductor.Shared.Config;
using Conductor.Shared.Types;
using Microsoft.IdentityModel.Tokens;

namespace Conductor.App.Database;

public abstract class DBExchange
{
    protected abstract string? QueryNonLocking();

    protected abstract string? QueryPagination(UInt64 current);

    protected abstract DbCommand CreateDbCommand(string query, DbConnection connection);

    public abstract DbConnection CreateConnection(string conStr);

    protected abstract string GetSqlType(Type dataType, Int32? lenght);

    protected abstract StringBuilder AddSurrogateKey(StringBuilder stringBuilder, string index, string tableName, string? file = null);

    protected abstract StringBuilder AddChangeColumn(StringBuilder stringBuilder, string tableName);

    protected abstract StringBuilder AddIdentityColumn(StringBuilder stringBuilder, string tableName);

    protected abstract StringBuilder AddColumnarStructure(StringBuilder stringBuilder, string tableName);

    protected abstract Task EnsureSchemaCreation(string system, DbConnection connection);

    public abstract Task<Result> WriteDataTable(DataTable data, Extraction extraction);

    public abstract Task<Result> WriteDataTable(DataTable data, Extraction extraction, DbConnection connection);

    protected abstract string GeneratePartitionCondition(Extraction extraction, double timeZoneOffSet, string? virtualColumn = null);

    protected virtual StringBuilder AddPrimaryKey(StringBuilder stringBuilder, string tableName)
    {
        return stringBuilder.Append($" CONSTRAINT IX_{tableName}_PK PRIMARY KEY (ID_DW_{tableName}),");
    }

    protected virtual string VirtualColumn(string tableName)
    {
        return $"{tableName}_{Settings.IndexFileGroupName}";
    }

    private async Task<Result<List<Extraction>>> GetDependencies(Extraction extraction)
    {
        string[] dependencies = extraction.Dependencies!.Split(Settings.SplitterChar);

        using var repository = new Data.LdbContext();
        using var service = new Service.ExtractionService(repository);

        var dependenciesList = await service.Search(dependencies);
        if (!dependenciesList.IsSuccessful) return dependenciesList.Error;

        dependenciesList.Value
            .ForEach(x =>
            {
                x.Origin!.ConnectionString = Shared.Encryption.SymmetricDecryptAES256(x.Origin!.ConnectionString, Settings.EncryptionKey);
                x.Destination!.ConnectionString = Shared.Encryption.SymmetricDecryptAES256(x.Destination!.ConnectionString, Settings.EncryptionKey);
            });

        return dependenciesList.Value;
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

    public virtual async Task<Result> ClearTable(Extraction extraction, DataTable data)
    {
        if (extraction.BeforeExecutionDeletes) return Result.Ok();

        using DbConnection connection = CreateConnection(extraction.Destination!.ConnectionString);
        await connection.OpenAsync();

        string tableName = extraction.Alias ?? extraction.Name;
        string schemaName = extraction.Origin!.Alias ?? extraction.Origin!.Name;
        StringBuilder builder = new();

        if (extraction.IsIncremental)
        {
            builder.Append($"DELETE FROM \"{schemaName}\".\"{tableName}\" WHERE 1 = 1 ");
            foreach (DataRow row in data.Rows)
            {
                if (extraction.IsVirtual) builder.Append($"AND \"{VirtualColumn(tableName)}\" = '{row[VirtualColumn(tableName)]}' ");
                builder.Append($"AND \"{extraction.IndexName}\" = {row[extraction.IndexName]} ");
            }
        }
        else
        {
            builder.Append($"TRUNCATE TABLE {schemaName}.{tableName}");
        }

        try
        {
            Log.Out($"Clearing table {schemaName}.{tableName}...");
            using DbCommand command = CreateDbCommand(builder.ToString(), connection);
            await command.ExecuteNonQueryAsync();
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

    public virtual async Task<Result<DataTable>> SingleFetch(
        Extraction extraction,
        UInt64 current,
        bool shouldPartition,
        string? virtualizedTable = null,
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
            columns = $"'{extraction.VirtualId}' AS \"{VirtualColumn(virtualizedTable)}\", *";
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
            var deps = await GetDependencies(extraction);
            if (!deps.IsSuccessful) return deps.Error;

            return await ParallelFetch(deps.Value, current, shouldPartition, extraction.Name, token);
        }
        else
        {
            return await SingleFetch(extraction, current, shouldPartition, extraction.Name, token);
        }
    }

    public virtual async Task<Result<DataTable>> ParallelFetch(
        List<Extraction> extractions,
        UInt64 current,
        bool shouldPartition,
        string virtualizedTable,
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
                var fetch = await SingleFetch(e, current, shouldPartition, virtualizedTable, t);
                if (!fetch.IsSuccessful)
                {
                    errCount++;
                    return;
                }

                bool isTemplate = e.IsVirtualTemplate ?? false;

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
                table.Constraints.Clear();
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
        queryBuilder = AddSurrogateKey(queryBuilder, extraction.IndexName, tableName, extraction.Dependencies);
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
        queryBuilder = AddSurrogateKey(queryBuilder, extraction.IndexName, tableName, extraction.Dependencies);
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