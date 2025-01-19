using Conductor.App;
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
    public async Task<Results<Ok<Message>, InternalServerError<Message<Error>>, JsonHttpResult<Message<Error>>>> ExecuteExtraction(IQueryCollection? filters)
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
                x.Destination!.DbString = Encryption.SymmetricDecryptAES256(x.Destination!.DbString, Settings.EncryptionKey);
            });

        var result = await ParallelExtractionManager.ChannelParallelize(
            fetch.Value,
            ParallelExtractionManager.ProduceDBData,
            ParallelExtractionManager.ConsumeDBData
        );

        if (!result.IsSuccessful)
        {
            if (result.Error.IsPartialSuccess)
            {
                return TypedResults.Json(
                    new Message<Error>(
                        Status207MultiStatus,
                        "The request has finished, but some errors have occurred.",
                        [result.Error],
                        true
                    ),
                    statusCode: Status207MultiStatus
                );
            }

            return TypedResults.InternalServerError(
                ErrorMessage("Extraction failed", result.Error)
            );
        }

        return TypedResults.Ok(
            new Message(Status200OK, "Extraction Successful.")
        );
    }
}