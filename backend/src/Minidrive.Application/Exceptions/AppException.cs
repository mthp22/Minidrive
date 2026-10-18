namespace Minidrive.Application.Exceptions;

/// <summary>
/// Base type for application-level failures that map onto a specific HTTP status. The API's
/// central exception handler translates these into Problem Details responses.
/// </summary>
public abstract class AppException : Exception
{
    protected AppException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    protected AppException(string code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    /// <summary>Stable, machine-readable error identifier.</summary>
    public string Code { get; }
}
