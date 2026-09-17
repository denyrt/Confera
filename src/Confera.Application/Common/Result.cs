namespace Confera.Application.Common;

/// <summary>An expected outcome with either a value or a failure, never both.</summary>
public sealed class Result<TValue, TError>
    where TValue : notnull
    where TError : notnull
{
    private readonly TValue? _value;
    private readonly TError? _error;

    private Result(bool isSuccess, TValue? value, TError? error)
    {
        IsSuccess = isSuccess;
        _value = value;
        _error = error;
    }

    public bool IsSuccess { get; }

    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("A failed result has no value.");

    public TError Error => !IsSuccess
        ? _error!
        : throw new InvalidOperationException("A successful result has no error.");

    public static Result<TValue, TError> Success(TValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(true, value, default);
    }

    public static Result<TValue, TError> Failure(TError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(false, default, error);
    }
}

/// <summary>An expected outcome for an operation without a success payload.</summary>
public sealed class Result<TError> where TError : notnull
{
    private readonly TError? _error;

    private Result(bool isSuccess, TError? error)
    {
        IsSuccess = isSuccess;
        _error = error;
    }

    public bool IsSuccess { get; }

    public TError Error => !IsSuccess
        ? _error!
        : throw new InvalidOperationException("A successful result has no error.");

    public static Result<TError> Success() => new(true, default);

    public static Result<TError> Failure(TError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(false, error);
    }
}
