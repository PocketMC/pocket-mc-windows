using System;

namespace PocketMC.Desktop.Core.Results;

public enum OperationFailureKind
{
    None = 0,
    ValidationFailed,
    NotFound,
    Conflict,
    Cancelled,
    Unauthorized,
    NetworkFailure,
    ProviderFailure,
    FileSystemFailure,
    CorruptData,
    Unsupported,
    Unknown
}

public sealed record OperationError(
    OperationFailureKind Kind,
    string Message,
    string? Code = null,
    Exception? Exception = null)
{
    public static OperationError Validation(string message, string? code = null) =>
        new(OperationFailureKind.ValidationFailed, message, code);

    public static OperationError Unknown(Exception exception, string? message = null, string? code = null) =>
        new(OperationFailureKind.Unknown, message ?? exception.Message, code, exception);
}

public record OperationResult
{
    public bool Success { get; init; }
    public OperationError? Error { get; init; }

    public static OperationResult Ok() => new() { Success = true };

    public static OperationResult Fail(OperationError error) => new() { Success = false, Error = error };
}

public sealed record OperationResult<T> : OperationResult
{
    public T? Value { get; init; }

    public static OperationResult<T> Ok(T value) => new() { Success = true, Value = value };

    public static new OperationResult<T> Fail(OperationError error) => new() { Success = false, Error = error };
}
