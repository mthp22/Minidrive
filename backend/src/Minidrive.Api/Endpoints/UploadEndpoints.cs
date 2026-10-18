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
/// Three-phase multipart upload endpoints: initiate, request pre-signed part URLs, complete (plus
/// abort). Clients only ever reference the server-side session id — the provider upload id and the
/// storage key never leave the server.
/// </summary>
public static class UploadEndpoints
{
    public static IEndpointRouteBuilder MapUploadEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var uploads = endpoints
            .MapGroup("/api/v1/uploads")
            .WithTags("Uploads")
            .RequireAuthorization();

        uploads.MapPost("/initiate", InitiateAsync)
            .WithName("InitiateUpload")
            .WithSummary("Reserves a file name, creates an upload session and starts the provider upload.")
            .Accepts<InitiateUploadRequest>("application/json")
            .Produces<InitiateUploadResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge);

        uploads.MapPost("/{uploadId}/parts/{partNumber}/url", CreatePartUrlAsync)
            .WithName("CreateUploadPartUrl")
            .WithSummary("Returns a pre-signed URL for uploading one part.")
            .Produces<PartUploadUrlResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        uploads.MapPost("/{uploadId}/complete", CompleteAsync)
            .WithName("CompleteUpload")
            .WithSummary("Completes the upload and finalises the file node.")
            .Accepts<CompleteUploadRequest>("application/json")
            .Produces<NodeDetails>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        uploads.MapPost("/{uploadId}/abort", AbortAsync)
            .WithName("AbortUpload")
            .WithSummary("Aborts an upload session, discarding uploaded parts and the reserved name.")
            .Accepts<AbortUploadRequest>("application/json")
            .Produces<AbortUploadResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> InitiateAsync(
        InitiateUploadRequest request,
        IValidator<InitiateUploadRequest> validator,
        ICurrentUser currentUser,
        UploadService uploads,
        CancellationToken cancellationToken)
    {
        await validator.ValidateRequestAsync(request, cancellationToken);

        var session = await uploads.InitiateAsync(currentUser.RequireUserId(), request, cancellationToken);

        // The file node exists (pending) as of this response, so it has a location.
        return Results.Created($"/api/v1/nodes/{session.FileId}", session);
    }

    private static async Task<IResult> CreatePartUrlAsync(
        string uploadId,
        int partNumber,
        ICurrentUser currentUser,
        UploadService uploads,
        CancellationToken cancellationToken)
    {
        var url = await uploads.CreatePartUrlAsync(currentUser.RequireUserId(), uploadId, partNumber, cancellationToken);
        return Results.Ok(url);
    }

    private static async Task<IResult> CompleteAsync(
        string uploadId,
        CompleteUploadRequest request,
        IValidator<CompleteUploadRequest> validator,
        ICurrentUser currentUser,
        UploadService uploads,
        CancellationToken cancellationToken)
    {
        await validator.ValidateRequestAsync(request, cancellationToken);

        var file = await uploads.CompleteAsync(currentUser.RequireUserId(), uploadId, request, cancellationToken);
        return Results.Ok(file);
    }

    private static async Task<IResult> AbortAsync(
        string uploadId,
        AbortUploadRequest? request,
        ICurrentUser currentUser,
        UploadService uploads,
        CancellationToken cancellationToken)
    {
        var result = await uploads.AbortAsync(
            currentUser.RequireUserId(),
            uploadId,
            request?.Reason,
            cancellationToken);

        return Results.Ok(result);
    }
}
