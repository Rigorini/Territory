namespace Territory.Application.Common;

public enum ErrorKind
{
    Validation,
    NotFound,
    Conflict,
    BusinessRule,
    Unauthorized,
    Unavailable
}

/// <summary>
/// Error identified by a stable <see cref="Code"/> that the client translates; field errors are codes as well.
/// </summary>
public sealed record AppError(ErrorKind Kind, string Code, IReadOnlyDictionary<string, string[]>? Fields = null)
{
    public static AppError NotFound(string code) => new(ErrorKind.NotFound, code);
    public static AppError Conflict(string code) => new(ErrorKind.Conflict, code);
    public static AppError BusinessRule(string code) => new(ErrorKind.BusinessRule, code);
    public static AppError Unauthorized(string code) => new(ErrorKind.Unauthorized, code);
    public static AppError Unavailable(string code) => new(ErrorKind.Unavailable, code);
    public static AppError Validation(IReadOnlyDictionary<string, string[]> fields) =>
        new(ErrorKind.Validation, "validation", fields);
}

public sealed class Result<T>
{
    private Result(T? value, AppError? error)
    {
        Value = value;
        Error = error;
    }

    public T? Value { get; }
    public AppError? Error { get; }
    public bool IsSuccess => Error is null;

    public static Result<T> Success(T value) => new(value, null);
    public static Result<T> Failure(AppError error) => new(default, error);

    public static implicit operator Result<T>(T value) => Success(value);
    public static implicit operator Result<T>(AppError error) => Failure(error);
}

/// <summary>Marker for operations without a return value.</summary>
public readonly record struct Unit
{
    public static readonly Unit Value = default;
}
