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

    protected abstract StringBuilder AddPrimaryKey(StringBuilder stringBuilder, string index, string tableName, string? file = null);

    protected abstract StringBuilder AddChangeColumn(StringBuilder stringBuilder, string tableName);

    protected abstract StringBuilder AddColumnarStructure(StringBuilder stringBuilder, string tableName);

    protected abstract Task EnsureSchemaCreation(string system, DbConnection connection);

    protected abstract Task<Result> BulkInsert(DataTable data, Extraction extraction);

    protected abstract string GeneratePartitionCondition(Extraction extraction);

    public virtual async Task<Result<bool>> Exists(Extraction extraction)
    {
        using DbConnection connection = CreateConnection(extraction.Destination!.ConnectionString);
        await connection.OpenAsync();

        using DbCommand command = CreateDbCommand(
            @$"
                SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES {QueryNonLocking()} 
                WHERE TABLE_NAME = '{extraction.Name}' AND TABLE_SCHEMA = '{extraction.Origin!.Name}'",
            connection
        );

        try
        {
            Log.Out($"Creating schema {extraction.Origin.Name}...");
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
        using DbCommand command = CreateDbCommand(
            @$"
                SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES {QueryNonLocking()} 
                WHERE TABLE_NAME = '{extraction.Name}' AND TABLE_SCHEMA = '{extraction.Origin!.Name}'",
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

        using DbCommand command = CreateDbCommand(
            $"SELECT COUNT(*) FROM  \"{extraction.Origin?.Name}\".\"{extraction.Name}\" {QueryNonLocking()}",
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

        string cmdText;
        if (!extraction.IsIncremental)
        {
            cmdText = $"TRUNCATE TABLE \"{extraction.Origin?.Name}\".\"{extraction.Name}\"";
        }
        else
        {
            if (extraction.FilterColumn.IsNullOrEmpty() || extraction.FilterTime == null)
            {
                return new Error("Invalid filter column, or value, input.");
            }

            cmdText =
                $"DELETE FROM \"{extraction.Origin?.Name}\".\"{extraction.Name}\" " +
                GeneratePartitionCondition(extraction);
        }

        using DbCommand command = CreateDbCommand(cmdText, connection);

        try
        {
            Log.Out($"Clearing table {extraction.Origin?.Name}.{extraction.Name}...");
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
        using DbConnection connection = CreateConnection(extraction.Destination!.ConnectionString);
        await connection.OpenAsync();

        using DbCommand command = CreateDbCommand(
            $"DROP TABLE \"{extraction.Origin?.Name}\".\"{extraction.Name}\"",
            connection
        );

        try
        {
            Log.Out($"Droping table {extraction.Origin?.Name}.{extraction.Name}...");
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
        string partitioning,
        CancellationToken token = default
    )
    {
        using DbConnection connection = CreateConnection(extraction.Origin!.ConnectionString);

        string name = extraction.Alias ?? extraction.Name;

        using DbCommand command = CreateDbCommand(
            $@"
                SELECT
                    *
                FROM {name} {QueryNonLocking() ?? ""}
                {partitioning}
                ORDER BY {extraction.IndexName} ASC
                {QueryPagination(current) ?? ""}
            ",
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
        bool moreThanZero,
        UInt64 current,
        CancellationToken token
    )
    {
        string partitioning = "";
        if (moreThanZero)
        {
            partitioning = extraction.IsIncremental ? GeneratePartitionCondition(extraction) : "";
        }

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

            return await ParallelFetch(dependenciesList.Value, current, partitioning, token);
        }
        else
        {
            return await SingleFetch(extraction, current, partitioning, token);
        }
    }

    public virtual async Task<Result<DataTable>> ParallelFetch(
        List<Extraction> extractions,
        UInt64 current,
        string partitioning,
        CancellationToken token
    )
    {
        ConcurrentBag<DataTable> dataTables = [];

        try
        {
            await Parallel.ForEachAsync(extractions, token, async (e, t) =>
            {
                var fetch = await SingleFetch(e, current, partitioning, t);
                if (!fetch.IsSuccessful) return;

                dataTables.Add(fetch.Value);
            });

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

        queryBuilder.AppendLine($"CREATE TABLE \"{extraction.Origin!.Name}\".\"{extraction.Name}\" (");

        foreach (DataColumn column in table.Columns)
        {
            Int32? maxStringLength = column.MaxLength;
            string SqlType = GetSqlType(column.DataType, maxStringLength);
            queryBuilder.AppendLine($"    \"{column.ColumnName}\" {SqlType},");
        }

        queryBuilder = AddChangeColumn(queryBuilder, extraction.Name);
        queryBuilder = AddPrimaryKey(queryBuilder, extraction.IndexName, extraction.Name, extraction.Dependencies);
        queryBuilder = AddColumnarStructure(queryBuilder, extraction.Name);
        queryBuilder.AppendLine(");");

        try
        {

            await EnsureSchemaCreation(extraction.Origin.Name, connection);

            Log.Out($"Creating table {extraction.Origin?.Name}.{extraction.Name}...");
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