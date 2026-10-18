using Minidrive.Application.Abstractions.Persistence;
using Minidrive.Application.Abstractions.Storage;
using Minidrive.Application.Common;
using Minidrive.Application.Exceptions;
using Minidrive.Application.Models;
using Minidrive.Application.Options;
using Microsoft.Extensions.Options;

namespace Minidrive.Application.Services;

/// <summary>
/// Read-side use cases for drive nodes: listing folder contents, reading metadata and producing
/// download URLs. Every query is scoped to the authenticated user.
/// </summary>
public sealed class NodeQueryService
{
    private readonly INodeRepository _nodes;
    private readonly IObjectStorageService _storage;
    private readonly UploadOptions _uploadOptions;

    public NodeQueryService(
        INodeRepository nodes,
        IObjectStorageService storage,
        IOptions<UploadOptions> uploadOptions)
    {
        _nodes = nodes;
        _storage = storage;
        _uploadOptions = uploadOptions.Value;
    }

    /// <summary>Lists the direct children of a folder (or the root when <paramref name="parentId"/> is null).</summary>
    public async Task<PagedResult<NodeSummary>> ListChildrenAsync(
        string userId,
        string? parentId,
        int? pageSize,
        string? cursorToken,
        CancellationToken cancellationToken)
    {
        if (parentId is not null)
        {
            var parent = await _nodes.FindAsync(userId, parentId, includePending: false, includeDeleted: false, cancellationToken);
            if (parent is null || !parent.IsFolder)
            {
                // A 404 (rather than 403) avoids disclosing that another user's folder exists.
                throw new NotFoundException("The requested folder was not found.");
            }
        }

        var size = PaginationLimits.Normalize(pageSize);
        var page = new PageRequest(size + 1, CursorCodec.Decode(cursorToken));
        var results = await _nodes.ListChildrenAsync(userId, parentId, page, cancellationToken);

        string? nextCursor = null;
        if (results.Count > size)
        {
            var lastReturned = results[size - 1];
            nextCursor = CursorCodec.Encode(new NodeCursor(lastReturned.Name, lastReturned.Id));
        }

        var items = results
            .Take(size)
            .Select(NodeModelMapper.ToSummary)
            .ToList();

        return new PagedResult<NodeSummary>(items, nextCursor);
    }

    /// <summary>Returns node metadata, optionally including the recursive size of a folder.</summary>
    public async Task<NodeDetails> GetNodeAsync(
        string userId,
        string nodeId,
        bool includeFolderSize,
        CancellationToken cancellationToken)
    {
        var node = await _nodes.FindAsync(userId, nodeId, includePending: false, includeDeleted: false, cancellationToken);
        if (node is null)
        {
            throw new NotFoundException("The requested node was not found.");
        }

        long? folderSize = null;
        if (includeFolderSize && node.IsFolder)
        {
            folderSize = await _nodes.SumDescendantFileSizeAsync(userId, node.DescendantPrefix, cancellationToken);
        }

        return NodeModelMapper.ToDetails(node, folderSize);
    }

    /// <summary>Creates a short-lived pre-signed URL for downloading a ready file.</summary>
    public async Task<DownloadUrlResponse> CreateDownloadUrlAsync(
        string userId,
        string nodeId,
        CancellationToken cancellationToken)
    {
        var node = await _nodes.FindAsync(userId, nodeId, includePending: false, includeDeleted: false, cancellationToken);
        if (node is null || !node.IsFile || string.IsNullOrEmpty(node.StorageKey))
        {
            throw new NotFoundException("The requested file was not found.");
        }

        var presigned = await _storage.CreatePresignedDownloadUrlAsync(
            node.StorageKey,
            node.Name,
            node.ContentType,
            _uploadOptions.PresignedUrlExpiration,
            cancellationToken);

        return new DownloadUrlResponse(node.Id, node.Name, presigned.Url, presigned.ExpiresAtUtc);
    }
}
