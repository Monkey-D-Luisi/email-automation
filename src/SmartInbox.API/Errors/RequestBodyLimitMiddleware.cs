using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace SmartInbox.API.Errors;

public sealed class RequestBodyLimitMiddleware(RequestDelegate next)
{
    public const long MaxRequestBodyBytes = 65_536;

    public async Task InvokeAsync(HttpContext context)
    {
        var requestBodySize = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (requestBodySize is { IsReadOnly: false })
            requestBodySize.MaxRequestBodySize = MaxRequestBodyBytes;

        if (context.Request.ContentLength > MaxRequestBodyBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            context.Response.ContentType = "application/problem+json";
            await JsonSerializer.SerializeAsync(context.Response.Body, new ProblemDetails
            {
                Type = "https://smartinbox.local/problems/request-too-large",
                Title = "Request body is too large",
                Status = StatusCodes.Status413PayloadTooLarge,
                Detail = $"The request body cannot exceed {MaxRequestBodyBytes} bytes.",
            }, cancellationToken: context.RequestAborted);
            return;
        }

        await next(context);
    }
}
