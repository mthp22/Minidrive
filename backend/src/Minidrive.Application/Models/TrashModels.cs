using Minidrive.Domain.Enums;

namespace Minidrive.Application.Models;

/// <summary>
/// A soft-deleted node as shown in the trash. It carries enough to identify the item and to know
/// when it will be permanently removed if it is not restored first.
/// </summary>
/// <param name="PurgeAfterUtc">
/// When background maintenance will permanently delete the item unless it is restored. Derived
/// from <paramref name="DeletedAtUtc"/> plus the configured retention window.
/// </param>
public sealed record TrashItem(
    string Id,
    string? ParentId,
    string Name,
    NodeType Type,
    long SizeBytes,
    string? ContentType,
    DateTimeOffset DeletedAtUtc,
    DateTimeOffset PurgeAfterUtc);
