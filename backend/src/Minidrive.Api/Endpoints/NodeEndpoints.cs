using FluentValidation;
using Minidrive.Api.Validation;
using Minidrive.Application.Abstractions.Security;
using Minidrive.Application.Common;
using Minidrive.Application.Models;
using Minidrive.Application.Services;
using Minidrive.Application.Validation;
using Microsoft.AspNetCore.Mvc;

namespace Minidrive.Api.Endpoints;

/// <summary>
/// Node endpoints: listing, metadata, rename, move, delete and download URLs.
/// </summary>
/// <remarks>
/// Every handler resolves the caller from the validated JWT, and every query and mutation is scoped
/// to that user. A node that belongs to someone else is reported as a 404, never a 403, so the API
/// does not disclose its existence.
/// </remarks>
public static class NodeEndpoints
{
    public static IEndpointRouteBuilder MapNodeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var nodes = endpoints
            .MapGroup("/api/v1/nodes")
            .WithTags("Nodes")
            .RequireAuthorization();

        nodes.MapGet(string.Empty, ListChildrenAsync)
            .WithName("ListNodes")
            .WithSummary("Lists the direct children of a folder (or of the drive root) using cursor pagination.")
            .Produces<PagedResult<NodeSummary>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        nodes.MapGet("/{nodeId}", GetNodeAsync)
            .WithName("GetNode")
            .WithSummary("Returns the metadata of a single node.")
            .Produces<NodeDetails>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        nodes.MapGet("/{nodeId}/download-url", CreateDownloadUrlAsync)
            .WithName("CreateDownloadUrl")
            .WithSummary("Returns a short-lived pre-signed URL for downloading a file.")
            .Produces<DownloadUrlResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        nodes.MapPatch("/{nodeId}", RenameNodeAsync)
            .WithName("RenameNode")
            .WithSummary("Renames a node.")
            .Accepts<RenameNodeRequest>("application/json")
            .Produces<NodeDetails>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        nodes.MapPost("/{nodeId}/move", MoveNodeAsync)
            .WithName("MoveNode")
            .WithSummary("Moves a node to another folder (or to the root when parentId is null).")
            .Accepts<MoveNodeRequest>("application/json")
            .Produces<NodeDetails>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        nodes.MapDelete("/{nodeId}", DeleteNodeAsync)
            .WithName("DeleteNode")
            .WithSummary("Soft-deletes a node and its subtree; storage objects are removed asynchronously.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        endpoints.MapGet("/api/v1/search", SearchNodesAsync)
            .WithTags("Search")
            .WithSummary("Case-insensitive name search across the user's folders and files.")
            .RequireAuthorization()
            .Produces<IReadOnlyList<NodeSummary>>()
            .ProducesValidationProblem();

        endpoints.MapPost("/api/v1/folders", CreateFolderAsync)
            .WithTags("Folders")
            .WithSummary("Creates a folder.")
            .RequireAuthorization()
            .Accepts<CreateFolderRequest>("application/json")
            .Produces<NodeDetails>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> ListChildrenAsync(
        ICurrentUser currentUser,
        NodeQueryService nodes,
        CancellationToken cancellationToken,
        [FromQuery] string? parentId,
        [FromQuery] int? pageSize,
        [FromQuery] string? cursor)
    {
        var page = await nodes.ListChildrenAsync(
            currentUser.RequireUserId(),
            parentId,
            pageSize,
            cursor,
            cancellationToken);

        return Results.Ok(page);
    }

    private static async Task<IResult> GetNodeAsync(
        string nodeId,
        ICurrentUser currentUser,
        NodeQueryService nodes,
        CancellationToken cancellationToken,
        [FromQuery] bool includeFolderSize = false)
    {
        var node = await nodes.GetNodeAsync(
            currentUser.RequireUserId(),
            nodeId,
            includeFolderSize,
            cancellationToken);

        return Results.Ok(node);
    }

    private static async Task<IResult> CreateDownloadUrlAsync(
        string nodeId,
        ICurrentUser currentUser,
        NodeQueryService nodes,
        CancellationToken cancellationToken)
    {
        var download = await nodes.CreateDownloadUrlAsync(currentUser.RequireUserId(), nodeId, cancellationToken);
        return Results.Ok(download);
    }

    private static async Task<IResult> CreateFolderAsync(
        CreateFolderRequest request,
        IValidator<CreateFolderRequest> validator,
        ICurrentUser currentUser,
        NodeCommandService commands,
        CancellationToken cancellationToken)
    {
        await validator.ValidateRequestAsync(request, cancellationToken);

        var folder = await commands.CreateFolderAsync(currentUser.RequireUserId(), request, cancellationToken);

        return Results.Created($"/api/v1/nodes/{folder.Id}", folder);
    }

    private static async Task<IResult> RenameNodeAsync(
        string nodeId,
        RenameNodeRequest request,
        IValidator<RenameNodeRequest> validator,
        ICurrentUser currentUser,
        NodeCommandService commands,
        CancellationToken cancellationToken)
    {
        await validator.ValidateRequestAsync(request, cancellationToken);

        var node = await commands.RenameNodeAsync(currentUser.RequireUserId(), nodeId, request, cancellationToken);
        return Results.Ok(node);
    }

    private static async Task<IResult> MoveNodeAsync(
        string nodeId,
        MoveNodeRequest request,
        IValidator<MoveNodeRequest> validator,
        ICurrentUser currentUser,
        NodeCommandService commands,
        CancellationToken cancellationToken)
    {
        await validator.ValidateRequestAsync(request, cancellationToken);

        var node = await commands.MoveNodeAsync(currentUser.RequireUserId(), nodeId, request, cancellationToken);
        return Results.Ok(node);
    }

    private static async Task<IResult> DeleteNodeAsync(
        string nodeId,
        ICurrentUser currentUser,
        NodeCommandService commands,
        CancellationToken cancellationToken)
    {
        await commands.DeleteNodeAsync(currentUser.RequireUserId(), nodeId, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> SearchNodesAsync(
        ICurrentUser currentUser,
        NodeQueryService nodes,
        IValidator<SearchNodesRequest> validator,
        CancellationToken cancellationToken,
        [FromQuery] string? q,
        [FromQuery] int? pageSize)
    {
        var request = new SearchNodesRequest(q, pageSize);
        await validator.ValidateRequestAsync(request, cancellationToken);

        var results = await nodes.SearchAsync(
            currentUser.RequireUserId(),
            request.Q!,
            pageSize,
            cancellationToken);

        return Results.Ok(results);
    }
}
