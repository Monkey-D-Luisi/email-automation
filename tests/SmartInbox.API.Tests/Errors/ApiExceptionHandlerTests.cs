using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SmartInbox.API.Errors;

namespace SmartInbox.API.Tests.Errors;

public sealed class ApiExceptionHandlerTests
{
    [Fact]
    public async Task Persistence_error_returns_generic_problem_without_exception_details()
    {
        const string sensitiveContent = "private-email-body-and-database-password";
        var context = new DefaultHttpContext();
        await using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;
        var handler = new ApiExceptionHandler(NullLogger<ApiExceptionHandler>.Instance);

        var handled = await handler.TryHandleAsync(
            context,
            new DbUpdateException(sensitiveContent),
            CancellationToken.None);

        responseBody.Position = 0;
        var response = await new StreamReader(responseBody).ReadToEndAsync();
        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Contains("internal-error", response, StringComparison.Ordinal);
        Assert.DoesNotContain(sensitiveContent, response, StringComparison.Ordinal);
    }
}
