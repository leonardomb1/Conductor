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

    protected abstract DbConnection CreateConnection(string conStr);

    protected abstract string GetSqlType(Type dataType, Int32? lenght);

    protected abstract StringBuilder AddSurrogateKey(StringBuilder stringBuilder, string index, string tableName, string? file = null);

    protected abstract StringBuilder AddChangeColumn(StringBuilder stringBuilder, string tableName);

    protected abstract StringBuilder AddIdentityColumn(StringBuilder stringBuilder, string tableName);

    protected abstract StringBuilder AddColumnarStructure(StringBuilder stringBuilder, string tableName);

    protected abstract Task EnsureSchemaCreation(string system, DbConnection connection);

    protected abstract Task<Result> BulkInsert(DataTable data, Extraction extraction);

    protected abstract string GeneratePartitionCondition(Extraction extraction);

    protected virtual StringBuilder AddPrimaryKey(StringBuilder stringBuilder, string tableName)
    {
        return stringBuilder.Append($" CONSTRAINT IX_{tableName}_PK PRIMARY KEY (ID_DW_{tableName}),");
    }

    protected virtual string VirtualColumn(string tableName)
    {
        return $"{tableName}_{Settings.IndexFileGroupName}";
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

    public virtual async Task<Result> ClearTable(Extraction extraction)
    {
        using DbConnection connection = CreateConnection(extraction.Destination!.ConnectionString);
        await connection.OpenAsync();

        string schemaName = extraction.Origin!.Alias ?? extraction.Origin!.Name;
        string tableName = extraction.Alias ?? extraction.Name;

        string cmdText;
        if (!extraction.IsIncremental)
        {
            cmdText = $"TRUNCATE TABLE \"{schemaName}\".\"{tableName}\"";
        }
        else
        {
            if (extraction.FilterColumn.IsNullOrEmpty() || extraction.FilterTime == null)
            {
                return new Error("Invalid filter column, or value, input.");
            }

            cmdText =
                $"DELETE FROM \"{schemaName}\".\"{tableName}\" " +
                GeneratePartitionCondition(extraction);
        }

        using DbCommand command = CreateDbCommand(cmdText, connection);

        try
        {
            Log.Out($"Clearing table {schemaName}.{tableName}...");
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
        CancellationToken token = default
    )
    {
        using DbConnection connection = CreateConnection(extraction.Origin!.ConnectionString);

        string partitioning = "";
        if (shouldPartition)
        {
            partitioning = extraction.IsIncremental ? GeneratePartitionCondition(extraction) : "";
        }

        string columns;
        if (extraction.IsVirtual)
        {
            columns = $"'{extraction.VirtualId}' AS \"{VirtualColumn(extraction.Alias ?? extraction.Name)}\", *";
        }
        else
        {
            columns = "*";
        }

        using DbCommand command = CreateDbCommand(
            @$"SELECT {columns} FROM {extraction.Name}
                {QueryNonLocking()}
                {partitioning}
            ORDER BY {extraction.IndexName} ASC
            {QueryPagination(current)}",
            connection
        );

        try
        {
            await connection.OpenAsync(token);

            using var fetched = new DataTable();
            var select = await command.ExecuteReaderAsync(token);
            fetched.Load(select);

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

            return await ParallelFetch(dependenciesList.Value, current, shouldPartition, token);
        }
        else
        {
            return await SingleFetch(extraction, current, shouldPartition, token);
        }
    }

    public virtual async Task<Result<DataTable>> ParallelFetch(
        List<Extraction> extractions,
        UInt64 current,
        bool shouldPartition,
        CancellationToken token
    )
    {
        ConcurrentBag<DataTable> dataTables = [];
        byte errCount = 0;

        try
        {
            await Parallel.ForEachAsync(extractions, token, async (e, t) =>
            {
                var fetch = await SingleFetch(e, current, shouldPartition, t);
                if (!fetch.IsSuccessful)
                {
                    errCount++;
                    return;
                }

                dataTables.Add(fetch.Value);
            });

            if (errCount == extractions.Count) return new Error("Failed to fetch data from all tables.");

            DataTable data = dataTables.FirstOrDefault()?.Clone() ?? new DataTable();

            foreach (var table in dataTables)
            {
                data.Merge(table, false, MissingSchemaAction.Ignore);
                table.Dispose();
            }

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

    public virtual async Task<Result> WriteDataTable(DataTable table, Extraction extraction)
    {
        var insert = await BulkInsert(table, extraction);
        if (!insert.IsSuccessful) return insert.Error;

        return Result.Ok();
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

        if (extraction.IsVirtual)
        {
            queryBuilder.AppendLine($"  {VirtualColumn(tableName)} {GetSqlType(typeof(string), Settings.VirtualTableIdMaxLenght)},");
        }

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
}