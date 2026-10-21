using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using SmartInbox.Application.Messages.ReceiveInboundMessage;

namespace SmartInbox.API.Errors;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is ValidationException validationException)
        {
            var validationProblem = CreateValidationProblem(validationException);
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            httpContext.Response.ContentType = "application/problem+json";
            await httpContext.Response.WriteAsJsonAsync(validationProblem, cancellationToken);
            return true;
        }

        var problem = exception switch
        {
            ExternalMessageConflictException => new ProblemDetails
            {
                Type = "https://smartinbox.local/problems/external-message-conflict",
                Title = "External message ID conflict",
                Detail = "The external message ID already exists with different content.",
                Status = StatusCodes.Status409Conflict,
            },
            BadHttpRequestException { StatusCode: StatusCodes.Status400BadRequest } => new ProblemDetails
            {
                Type = "https://smartinbox.local/problems/invalid-request",
                Title = "Request body is invalid",
                Status = StatusCodes.Status400BadRequest,
            },
            BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } => new ProblemDetails
            {
                Type = "https://smartinbox.local/problems/request-too-large",
                Title = "Request body is too large",
                Status = StatusCodes.Status413PayloadTooLarge,
            },
            BadHttpRequestException { StatusCode: StatusCodes.Status415UnsupportedMediaType } => new ProblemDetails
            {
                Type = "https://smartinbox.local/problems/unsupported-media-type",
                Title = "Unsupported media type",
                Status = StatusCodes.Status415UnsupportedMediaType,
            },
            _ => CreateInternalProblem(exception),
        };

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        httpContext.Response.ContentType = "application/problem+json";
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }

    private static HttpValidationProblemDetails CreateValidationProblem(ValidationException exception)
    {
        var errors = exception.Errors
            .GroupBy(failure => failure.PropertyName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(failure => failure.ErrorMessage).Distinct().ToArray());
        return new HttpValidationProblemDetails(errors)
        {
            Type = "https://smartinbox.local/problems/validation",
            Title = "Request validation failed",
            Status = StatusCodes.Status400BadRequest,
        };
    }

    private ProblemDetails CreateInternalProblem(Exception exception)
    {
        logger.LogError("Request failed with exception type {ExceptionType}.", exception.GetType().Name);
        return new ProblemDetails
        {
            Type = "https://smartinbox.local/problems/internal-error",
            Title = "An unexpected error occurred",
            Status = StatusCodes.Status500InternalServerError,
        };
    }
}
