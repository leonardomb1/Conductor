using System.Text;
using Conductor.Model;
using Conductor.Repository;
using Conductor.Shared;
using Conductor.Types;
using Serilog;
using static Microsoft.AspNetCore.Http.StatusCodes;

namespace Conductor.Controller;

public abstract class ControllerBase<TModel>(IRepository<TModel> repository) where TModel : IDbModel
{
    protected Message<Error> ErrorMessage(Error? err = null)
    {
        Message<Error> errMsg = new(
                Status500InternalServerError,
                "An internal error has occured while trying to proccess the request.",
                values: [err],
                err: true
        );

        Log.Error(
            $@"An error occurred while processing a request:
                Message: {err?.ExceptionMessage}
                Faulted Method: {err?.FaultedMethod}
                Trace: {err?.StackTrace}"
        );

        if (!Settings.DevelopmentMode)
        {
            errMsg.Content = null;
            errMsg.EntityCount = null;
        }

        return errMsg;
    }

    protected Message<Error> ErrorMessage(List<Error> err)
    {
        Message<Error> errMsg = new(
                Status500InternalServerError,
                "An internal error has occured while trying to proccess the request.",
                values: err,
                err: true
        );

        StringBuilder builder = new("Some errors have occurred while processing a long running request: ");

        for (Int32 i = 0; i < err.Count; i++)
        {
            builder.Append(@$"Error {i}:
                Message: {err[i].ExceptionMessage}
                Faulted Method: {err[i].FaultedMethod}
                Trace: {err[i].StackTrace}");
        }

        Log.Error(builder.ToString());

        if (!Settings.DevelopmentMode)
        {
            errMsg.Content = null;
            errMsg.EntityCount = null;
        }

        return errMsg;
    }

    public virtual async Task<IResult> Get(IQueryCollection? filters)
    {
        var result = await repository.Search(filters);

        if (!result.IsSuccessful)
        {
            return Results.InternalServerError(
                ErrorMessage(result.Error)
            );
        }

        return Results.Ok(
            new Message<TModel>(Status200OK, "OK", result.Value)
        );
    }

    public virtual async Task<IResult> GetById(string stringId)
    {
        if (!UInt32.TryParse(stringId, out UInt32 id))
        {
            return Results.BadRequest(
                new Message(Status400BadRequest, "This is an invalid parameter type.")
            );
        }

        var result = await repository.Search(id);

        if (!result.IsSuccessful)
        {
            return Results.InternalServerError(
                ErrorMessage(result.Error)
            );
        }

        if (result.Value is null)
        {
            return Results.Ok(
            new Message(Status200OK, "Requested resource was not found.")
        );
        }

        return Results.Ok(
            new Message<TModel>(Status200OK, "OK", [result.Value])
        );
    }

    public virtual async Task<IResult> Post(Stream body)
    {
        var deserialize = await Converter.TryDeserializeJson<TModel>(body);

        if (!deserialize.IsSuccessful)
        {
            return Results.BadRequest(
                new Message(Status400BadRequest, $"This is an invalid JSON format for this operation: {deserialize.Error.ExceptionMessage}")
            );
        }

        var result = await repository.Create(deserialize.Value);

        if (!result.IsSuccessful)
        {
            return Results.InternalServerError(
                ErrorMessage(result.Error)
            );
        }

        return Results.Created(
            $"/{result.Value}",
            new Message(Status201Created, $"Resource available at /{result.Value}")
        );
    }

    public virtual async Task<IResult> Put(string stringId, Stream body)
    {
        if (!UInt32.TryParse(stringId, out UInt32 id))
        {
            return Results.BadRequest(
                new Message(Status400BadRequest, "This is an invalid parameter type.", true)
            );
        }

        var deserialize = await Converter.TryDeserializeJson<TModel>(body);

        if (!deserialize.IsSuccessful)
        {
            return Results.BadRequest(
                new Message(Status400BadRequest, "This is an invalid JSON format for this operation.", true)
            );
        }

        var result = await repository.Update(deserialize.Value, id);

        if (!result.IsSuccessful)
        {
            return Results.InternalServerError(
                ErrorMessage(result.Error)
            );
        }

        return Results.NoContent();
    }

    public virtual async Task<IResult> Delete(string stringId)
    {
        if (!UInt32.TryParse(stringId, out UInt32 id))
        {
            return Results.BadRequest(
                new Message(Status400BadRequest, "This is an invalid parameter type.", true)
            );
        }

        var result = await repository.Delete(id);

        if (!result.IsSuccessful)
        {
            return Results.InternalServerError(
                ErrorMessage(result.Error)
            );
        }

        return Results.NoContent();
    }
}