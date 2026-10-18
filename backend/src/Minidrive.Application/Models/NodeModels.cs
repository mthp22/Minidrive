using Minidrive.Domain.Enums;

namespace Minidrive.Application.Models;

/// <summary>List/card representation of a node. Internal storage details are never exposed.</summary>
public sealed record NodeSummary(
    string Id,
    string? ParentId,
    string Name,
    NodeType Type,
    long SizeBytes,
    string? ContentType,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>Detailed representation of a node.</summary>
public sealed record NodeDetails(
    string Id,
    string? ParentId,
    string Name,
    NodeType Type,
    long SizeBytes,
    string? ContentType,
    string? ETag,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    long? FolderSizeBytes);
