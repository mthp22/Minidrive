using System.Security.Authentication;
using Minidrive.Application.Exceptions;
using Minidrive.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Minidrive.Api.Errors;

/// <summary>
/// Translates application and domain failures into RFC 7807 Problem Details responses.
/// </summary>
/// <remarks>
/// Ownership failures surface as 404 rather than 403 so the API never discloses that another
/// user's data exists. Unhandled exceptions become a generic 500: stack traces, connection
/// strings and provider details stay in the server logs.
/// </remarks>
public sealed class ApiExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetails;
    private readonly ILogger<ApiExceptionHandler> _logger;

    public ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(problemDetails);
        ArgumentNullException.ThrowIfNull(logger);

        _problemDetails = problemDetails;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, code, title) = Map(exception);

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception while handling {Method} {Path}.",
                httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            _logger.LogInformation(exception, "Request failed with {StatusCode} ({ErrorCode}).", statusCode, code);
        }

        httpContext.Response.StatusCode = statusCode;

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = exception.Message,
            Type = $"https://minidrive.dev/problems/{code}",
            Instance = httpContext.Request.Path,
        };

        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;

        AddDetails(problem, exception);

        await _problemDetails.WriteAsync(
            new ProblemDetailsContext
            {
                HttpContext = httpContext,
                Exception = exception,
                ProblemDetails = problem,
            });

        return true;
    }

    private static void AddDetails(ProblemDetails problem, Exception exception)
    {
        switch (exception)
        {
            case ValidationFailedException validation:
                problem.Extensions["errors"] = validation.Errors;
                break;

            case QuotaExceededException quota:
                problem.Extensions["quotaBytes"] = quota.QuotaBytes;
                problem.Extensions["usedBytes"] = quota.UsedBytes;
                problem.Extensions["requestedBytes"] = quota.RequestedBytes;
                break;

            case InvalidUploadStateTransitionException transition:
                problem.Extensions["from"] = transition.From.ToString();
                problem.Extensions["to"] = transition.To.ToString();
                break;

            case NameConflictException conflict:
                problem.Extensions["name"] = conflict.Name;
                break;
        }
    }

    private static (int StatusCode, string Code, string Title) Map(Exception exception) => exception switch
    {
        AuthenticationRequiredException => (StatusCodes.Status401Unauthorized, AuthenticationRequiredException.ErrorCode, "Authentication required"),
        NotFoundException => (StatusCodes.Status404NotFound, NotFoundException.ErrorCode, "Resource not found"),
        ValidationFailedException => (StatusCodes.Status400BadRequest, ValidationFailedException.ErrorCode, "Validation failed"),
        ConflictException conflict => (StatusCodes.Status409Conflict, conflict.Code, "Conflict"),
        NameConflictException => (StatusCodes.Status409Conflict, NameConflictException.ErrorCode, "Name conflict"),
        InvalidMoveException => (StatusCodes.Status409Conflict, InvalidMoveException.ErrorCode, "Invalid move"),
        InvalidUploadStateTransitionException => (StatusCodes.Status409Conflict, InvalidUploadStateTransitionException.ErrorCode, "Invalid upload state"),
        InvalidNodeNameException => (StatusCodes.Status400BadRequest, InvalidNodeNameException.ErrorCode, "Invalid node name"),
        PayloadTooLargeException => (StatusCodes.Status413PayloadTooLarge, PayloadTooLargeException.ErrorCode, "Payload too large"),
        QuotaExceededException => (507, QuotaExceededException.ErrorCode, "Storage quota exceeded"),
        StorageUnavailableException => (StatusCodes.Status502BadGateway, StorageUnavailableException.ErrorCode, "Object storage unavailable"),
        AuthenticationException => (StatusCodes.Status401Unauthorized, AuthenticationRequiredException.ErrorCode, "Authentication failed"),
        BadHttpRequestException badRequest => (badRequest.StatusCode, "request.malformed", "The request could not be parsed"),
        _ => (StatusCodes.Status500InternalServerError, "error.internal", "An unexpected error occurred"),
    };
}
