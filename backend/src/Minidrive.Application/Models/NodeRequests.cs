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
