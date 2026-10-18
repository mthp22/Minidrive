namespace Minidrive.Domain.Exceptions;

/// <summary>
/// Base type for violations of a domain invariant. The <see cref="Code"/> is a stable,
/// machine-readable identifier that the API maps onto a Problem Details response.
/// </summary>
public class DomainException : Exception
{
    public DomainException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public DomainException(string code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    /// <summary>Stable error identifier, e.g. <c>node.name.invalid</c>.</summary>
    public string Code { get; }
}
