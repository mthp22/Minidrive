namespace Minidrive.Application.Models;

/// <summary>Creates a folder. <see cref="ParentId"/> is null for the drive root.</summary>
/// <param name="ParentId">Destination folder, or null for the root.</param>
/// <param name="Name">Requested folder name.</param>
public sealed record CreateFolderRequest(string? ParentId, string Name);

/// <summary>Partial metadata update. Only the name is mutable through PATCH.</summary>
/// <param name="Name">New node name.</param>
public sealed record RenameNodeRequest(string Name);

/// <summary>Reparents a node. <see cref="ParentId"/> is null to move to the drive root.</summary>
/// <param name="ParentId">Target folder, or null for the root.</param>
public sealed record MoveNodeRequest(string? ParentId);

/// <summary>Searches the user's nodes by name (case-insensitive substring match).</summary>
/// <param name="Q">Raw search text; always treated literally, never as a pattern.</param>
/// <param name="PageSize">Maximum number of results (clamped to the documented range).</param>
public sealed record SearchNodesRequest(string? Q, int? PageSize);
