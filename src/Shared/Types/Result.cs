namespace Conductor.Shared.Types;

public readonly struct Result<T>
{
    private readonly bool success;
    public readonly T Value;
    public readonly Error Error;

    private Result(T value, Error error, bool isSuccess)
    {
        Value = value;
        Error = error;
        success = isSuccess;
    }

    public bool IsSuccessful => success;

    public static Result<T> Ok(T value) => new(value, null!, true);

    public static Result<T> Err(Error error) => new(default!, error, false);

    public static implicit operator Result<T>(T value) => new(value, null!, true);
    public static implicit operator Result<T>(Error error) => new(default!, error, false);

    public R Match<R>(
        Func<T, R> onSuccess,
        Func<Error, R> onError
    ) => success ? onSuccess(Value) : onError(Error);
}

public readonly struct Result
{
    private readonly bool success;
    public readonly Error Error;

    private Result(Error error, bool isSuccess)
    {
        Error = error;
        success = isSuccess;
    }

    public bool IsSuccessful => success;

    public static Result Ok() => new(null!, true);

    public static Result Err(Error error) => new(error, false);

    public static implicit operator Result(Error error) => new(error, false);

    public R Match<R>(
        Func<R> onSuccess,
        Func<Error, R> onError
    ) => success ? onSuccess() : onError(Error);
}