namespace SchoolLibrary.Services.Common;

/// <summary>Why a service operation failed. Controllers map this to an HTTP response.</summary>
public enum ServiceError
{
    None,
    NotFound,
    Conflict,
    Invalid,
    Forbidden
}

/// <summary>
/// Outcome of a service operation. Expected failures (not found, duplicate name, invalid input)
/// are returned as values instead of being thrown as exceptions.
/// </summary>
public class ServiceResult
{
    protected ServiceResult(ServiceError error, string? message)
    {
        Error = error;
        Message = message;
    }

    public ServiceError Error { get; }

    /// <summary>User-facing message (Bulgarian) when the operation failed.</summary>
    public string? Message { get; }

    public bool Succeeded => Error == ServiceError.None;

    public static ServiceResult Success() => new(ServiceError.None, null);

    public static ServiceResult Failure(ServiceError error, string message) => new(error, message);
}

/// <summary>A <see cref="ServiceResult"/> that also carries a value when it succeeded.</summary>
public class ServiceResult<T> : ServiceResult
{
    private ServiceResult(ServiceError error, string? message, T? value)
        : base(error, message)
    {
        Value = value;
    }

    public T? Value { get; }

    public static ServiceResult<T> Success(T value) => new(ServiceError.None, null, value);

    public new static ServiceResult<T> Failure(ServiceError error, string message) => new(error, message, default);
}
