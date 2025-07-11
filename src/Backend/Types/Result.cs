namespace Conductor.Types;

public readonly struct Result<T>
{
    private readonly bool success;
    private readonly T value;
    private readonly Error singleError;
    private readonly Error[] multipleErrors;

    private Result(T val, Error err, Error[] errs, bool ok)
    {
        value = val;
        singleError = err;
        multipleErrors = errs;
        success = ok;
    }

    public bool IsSuccessful => success;
    public T Value => value;
    public Error Error => singleError;
    public ReadOnlySpan<Error> Errors => multipleErrors ?? (singleError is not null ? [singleError] : Array.Empty<Error>());
    public bool HasMultipleErrors => multipleErrors is not null;

    public static Result<T> Ok(T value) => new(value, null!, null!, true);
    public static Result<T> Err(Error error) => new(default!, error, null!, false);
    public static Result<T> Err(params Error[] errors) => new(default!, null!, errors, false);
    public static Result<T> Err(IEnumerable<Error> errors) => new(default!, null!, errors.ToArray(), false);

    public static implicit operator Result<T>(T value) => Ok(value);
    public static implicit operator Result<T>(Error error) => Err(error);
    public static implicit operator Result<T>(Error[] errors) => Err(errors);

    public R Match<R>(Func<T, R> onSuccess, Func<Error, R> onError)
        => success ? onSuccess(value) : onError(singleError ?? multipleErrors[0]);

    public R Match<R>(Func<T, R> onSuccess, Func<ReadOnlySpan<Error>, R> onErrors)
        => success ? onSuccess(value) : onErrors(Errors);
}

public readonly struct Result
{
    private readonly bool success;
    private readonly Error singleError;
    private readonly Error[] multipleErrors;

    private Result(Error err, Error[] errs, bool ok)
    {
        singleError = err;
        multipleErrors = errs;
        success = ok;
    }

    public bool IsSuccessful => success;
    public Error Error => singleError;
    public ReadOnlySpan<Error> Errors => multipleErrors ?? (singleError is not null ? [singleError] : Array.Empty<Error>());
    public bool HasMultipleErrors => multipleErrors is not null;
    public static Result Ok() => new(null!, null!, true);
    public static Result Err(Error error) => new(error, null!, false);
    public static Result Err(params Error[] errors) => new(null!, errors, false);
    public static Result Err(IEnumerable<Error> errors) => new(null!, [.. errors], false);

    public static implicit operator Result(Error error) => Err(error);
    public static implicit operator Result(Error[] errors) => Err(errors);

    public R Match<R>(Func<R> onSuccess, Func<Error, R> onError)
        => success ? onSuccess() : onError(singleError ?? multipleErrors[0]);

    public R Match<R>(Func<R> onSuccess, Func<ReadOnlySpan<Error>, R> onErrors)
        => success ? onSuccess() : onErrors(Errors);
}