using System.Runtime.CompilerServices;
using Conductor.Logging;
using Conductor.Model;
using Conductor.Service;
using Conductor.Shared;
using Conductor.Shared.Config;
using Conductor.Shared.Types;
using Microsoft.AspNetCore.Http.HttpResults;
using static Microsoft.AspNetCore.Http.StatusCodes;

namespace Conductor.Controller;

public abstract class ControllerBase<TModel>(IService<TModel> service) where TModel : IDbModel
{
    protected Message<Error> ErrorMessage(string msg, Error? err = null, [CallerMemberName] string? method = null)
    {
        Message<Error> errMsg = new(
                Status500InternalServerError,
                msg,
                values: [err],
                err: true
        );

        Log.Out($"An error occurred while processing a request: {err?.ExceptionMessage}.\nTrace: {err?.StackTrace}", callerMethod: method);

        if (!Settings.DevelopmentMode)
        {
            errMsg.Content = null;
            errMsg.EntityCount = null;
        }

        return errMsg;
    }

    public virtual async ValueTask<Results<Ok<Message<TModel>>, InternalServerError<Message<Error>>, BadRequest<Message>>> Get(IQueryCollection? filters)
    {

        if (filters?.Count > Settings.MaxQueryParams)
        {
            return TypedResults.BadRequest(
                new Message(Status400BadRequest, "Parameter limit has been hit.", true)
            );
        }

        var result = await service.Search(filters);

        if (!result.IsSuccessful)
        {
            return TypedResults.InternalServerError(
                ErrorMessage("Failed to fetch data from Db.", result.Error)
            );
        }

        return TypedResults.Ok(
            new Message<TModel>(Status200OK, "Data fetch successful.", result.Value)
        );
    }

    public virtual async ValueTask<Results<Ok<Message>, Ok<Message<TModel>>, InternalServerError<Message<Error>>, BadRequest<Message>>> GetById(string stringId)
    {
        if (!UInt32.TryParse(stringId, out UInt32 id))
        {
            return TypedResults.BadRequest(
                new Message(Status400BadRequest, "This is an invalid parameter type.")
            );
        }

        var result = await service.Search(id);

        if (!result.IsSuccessful)
        {
            return TypedResults.InternalServerError(
                ErrorMessage("Failed to fetch data from Db.", result.Error)
            );
        }

        if (result.Value is null)
        {
            return TypedResults.Ok(
            new Message(Status200OK, "Result set is empty.")
        );
        }

        return TypedResults.Ok(
            new Message<TModel>(Status200OK, "Data fetch successful.", [result.Value])
        );
    }

    public virtual async ValueTask<Results<Created<Message>, BadRequest<Message>, InternalServerError<Message<Error>>>> Post(Stream body)
    {
        var deserialize = await Converter.TryDeserializeJson<TModel>(body);

        if (!deserialize.IsSuccessful)
        {
            return TypedResults.BadRequest(
                new Message(Status400BadRequest, $"This is an invalid JSON format for this operation. {deserialize.Error.ExceptionMessage} {deserialize.Error.StackTrace}")
            );
        }

        var result = await service.Create(deserialize.Value);

        if (!result.IsSuccessful)
        {
            return TypedResults.InternalServerError(
                ErrorMessage("Failed to execute insert into db.", result.Error)
            );
        }

        return TypedResults.Created(
            "",
            new Message(Status201Created, "Created successfully.")
        );
    }

    public virtual async ValueTask<Results<Ok<Message>, BadRequest<Message>, InternalServerError<Message<Error>>>> Put(string stringId, Stream body)
    {
        if (!UInt32.TryParse(stringId, out UInt32 id))
        {
            return TypedResults.BadRequest(
                new Message(Status400BadRequest, "This is an invalid parameter type.", true)
            );
        }

        var deserialize = await Converter.TryDeserializeJson<TModel>(body);

        if (!deserialize.IsSuccessful)
        {
            return TypedResults.BadRequest(
                new Message(Status400BadRequest, "This is an invalid JSON format for this operation.", true)
            );
        }

        var result = await service.Update(deserialize.Value, id);

        if (!result.IsSuccessful)
        {
            return TypedResults.InternalServerError(
                ErrorMessage("Failed to execute update for the selected id.", result.Error)
            );
        }

        return TypedResults.Ok(
            new Message(Status200OK, "Updated Successfully.")
        );
    }

    public virtual async ValueTask<Results<Ok<Message>, BadRequest<Message>, InternalServerError<Message<Error>>>> Delete(string stringId)
    {
        if (!UInt32.TryParse(stringId, out UInt32 id))
        {
            return TypedResults.BadRequest(
                new Message(Status400BadRequest, "This is an invalid parameter type.", true)
            );
        }

        var result = await service.Delete(id);

        if (!result.IsSuccessful)
        {
            return TypedResults.InternalServerError(
                ErrorMessage("Failed to delete specified registry in db.", result.Error)
            );
        }

        return TypedResults.Ok(
            new Message(Status200OK, "Deleted Successfully.")
        );
    }
}