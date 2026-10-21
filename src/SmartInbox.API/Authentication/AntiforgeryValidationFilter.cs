using Microsoft.AspNetCore.Antiforgery;

namespace SmartInbox.API.Authentication;

public sealed class AntiforgeryValidationFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;
        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) ||
            HttpMethods.IsOptions(request.Method) || HttpMethods.IsTrace(request.Method))
            return await next(context);

        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.Problem(
                type: "https://smartinbox.local/problems/antiforgery-validation-failed",
                title: "Request verification failed",
                statusCode: StatusCodes.Status400BadRequest);
        }
        return await next(context);
    }
}
