namespace Minidrive.Application.Exceptions;

/// <summary>
/// Thrown when object storage cannot be reached or returns an unexpected failure. The API maps
/// this to a 502 so callers can distinguish an upstream outage from their own request errors.
/// </summary>
public sealed class StorageUnavailableException : AppException
{
    public const string ErrorCode = "storage.unavailable";

    public StorageUnavailableException(string message, Exception innerException)
        : base(ErrorCode, message, innerException)
    {
    }
}
