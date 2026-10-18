namespace Minidrive.Domain.Enums;

/// <summary>
/// Lifecycle of a file node. A file is <see cref="Pending"/> between upload initiation and
/// successful completion; folders are always <see cref="Ready"/>.
/// Pending nodes are intentionally excluded from normal listings and metadata lookups.
/// </summary>
public enum NodeStatus
{
    Pending = 0,
    Ready = 1,
}
