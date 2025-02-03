using System.Data;
using Conductor.App;
using Conductor.App.Database;
using Conductor.Model;
using Conductor.Service;
using Conductor.Shared;
using Conductor.Shared.Config;
using Conductor.Shared.Types;
using Microsoft.AspNetCore.Http.HttpResults;
using static Microsoft.AspNetCore.Http.StatusCodes;

namespace Conductor.Controller;

public sealed class ExtractionController(ExtractionService service) : ControllerBase<Extraction>(service)
{
    public async Task<Results<Ok<Message>, InternalServerError<Message<Error>>>> ExecuteExtraction(IQueryCollection? filters)
    {
        var fetch = await service.Search(filters);

        if (!fetch.IsSuccessful)
        {
            return TypedResults.InternalServerError(ErrorMessage(
                fetch.Error.ExceptionMessage)
            );
        }

        fetch.Value
            .ForEach(x =>
            {
                x.Origin!.ConnectionString = Encryption.SymmetricDecryptAES256(x.Origin!.ConnectionString, Settings.EncryptionKey);
                x.Destination!.ConnectionString = Encryption.SymmetricDecryptAES256(x.Destination!.ConnectionString, Settings.EncryptionKey);
            });

        var result = await ParallelExtractionManager.ChannelParallelize(
            fetch.Value,
            ParallelExtractionManager.ProduceDBData,
            ParallelExtractionManager.ConsumeDBData
        );

        if (!result.IsSuccessful)
        {
            return TypedResults.InternalServerError(
                ErrorMessage("Extraction failed", result.Error)
            );
        }

        return TypedResults.Ok(
            new Message(Status200OK, "Extraction Successful.")
        );
    }

    public async Task<Results<Ok<Message>, InternalServerError<Message<Error>>, Ok<Message<Dictionary<string, object>>>>> FetchData(IQueryCollection? filters)
    {
        var fetch = await service.Search(filters);

        if (!fetch.IsSuccessful)
        {
            return TypedResults.InternalServerError(ErrorMessage(
                fetch.Error.ExceptionMessage)
            );
        }

        var res = fetch.Value.FirstOrDefault();

        if (res == null)
        {
            return TypedResults.Ok(new Message(Status200OK, "No such table.", false));
        }

        res.Origin!.ConnectionString = Encryption.SymmetricDecryptAES256(
            res.Origin!.ConnectionString,
            Settings.EncryptionKey
        );

        res.Destination!.ConnectionString = Encryption.SymmetricDecryptAES256(
            res.Destination!.ConnectionString,
            Settings.EncryptionKey
        );

        var engine = DBExchangeFactory.Create(res.Origin.DbType);
        var query = await engine.FetchDataTable(res, false, 0, default, shouldPaginate: false);
        if (!query.IsSuccessful)
        {
            return TypedResults.InternalServerError(ErrorMessage(
                fetch.Error.ExceptionMessage)
            );
        }

        using var dataTable = query.Value;
        List<Dictionary<string, object>> rows = [.. dataTable.Rows.Cast<DataRow>().Select(row =>
            dataTable.Columns.Cast<DataColumn>().ToDictionary(
                col => col.ColumnName,
                col => row[col]
            )
        )];

        return TypedResults.Ok(
            new Message<Dictionary<string, object>>(Status200OK, "Result fetch was successful.", (dynamic)rows)
        );
    }

    public async Task<Results<Ok<Message>, InternalServerError<Message<Error>>, BadRequest<Message>>> DropPhysicalTable(string stringId)
    {
        if (!UInt32.TryParse(stringId, out UInt32 id))
        {
            return TypedResults.BadRequest(
                new Message(Status400BadRequest, "This is an invalid parameter type.", true)
            );
        }

        var fetch = await service.Search(id);

        if (!fetch.IsSuccessful)
        {
            return TypedResults.InternalServerError(ErrorMessage(
                fetch.Error.ExceptionMessage)
            );
        }

        if (fetch.Value == null)
        {
            return TypedResults.Ok(
                new Message(Status200OK, "No such table.")
            );
        }

        fetch.Value.Origin!.ConnectionString = Encryption.SymmetricDecryptAES256(
            fetch.Value.Origin!.ConnectionString,
            Settings.EncryptionKey
        );

        fetch.Value.Destination!.ConnectionString = Encryption.SymmetricDecryptAES256(
            fetch.Value.Destination!.ConnectionString,
            Settings.EncryptionKey
        );

        var engine = DBExchangeFactory.Create(fetch.Value.Destination.DbType);

        var result = await engine.DropTable(fetch.Value);

        if (!result.IsSuccessful)
        {
            return TypedResults.InternalServerError(
                ErrorMessage("Drop table has failed.", result.Error)
            );
        }

        return TypedResults.Ok(
            new Message(Status200OK, "Table has been dropped.")
        );
    }
}