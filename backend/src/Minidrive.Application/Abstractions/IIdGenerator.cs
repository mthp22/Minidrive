namespace Minidrive.Application.Abstractions;

/// <summary>
/// Generates server-side identifiers. Abstracted so tests can produce deterministic ids and so
/// the domain stays free of any MongoDB <c>ObjectId</c> dependency.
/// </summary>
public interface IIdGenerator
{
    /// <summary>Returns a new, unique identifier.</summary>
    string NewId();
}
