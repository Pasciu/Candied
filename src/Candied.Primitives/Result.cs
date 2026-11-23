namespace Candied.Primitives;

public readonly struct Result<TValue, TError>
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;

    private readonly TValue? _value;
    private readonly TError? _error;

    internal Result(TValue value)
    {
        IsSuccess = true;
        _value = value;
        _error = default;
    }

    internal Result(TError error)
    {
        IsSuccess = false;
        _value = default;
        _error = error;
    }

    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access Value on a failed result.");

    public TError Error => IsFailure
        ? _error!
        : throw new InvalidOperationException("Cannot access Error on a successful result.");

    public static implicit operator Result<TValue, TError>(TValue value) => Result.Ok<TValue, TError>(value);
    public static implicit operator Result<TValue, TError>(TError error) => Result.Fail<TValue, TError>(error);
}

public static class Result
{
    public static Result<TValue, TError> Ok<TValue, TError>(TValue value) => new(value);
    public static Result<TValue, TError> Fail<TValue, TError>(TError error) => new(error);
}

public readonly record struct Unit;
