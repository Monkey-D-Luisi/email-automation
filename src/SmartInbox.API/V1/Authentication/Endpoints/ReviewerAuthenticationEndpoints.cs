using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Options;
using SmartInbox.API.Authentication;
using SmartInbox.API.V1.Authentication.Contracts;
using SmartInbox.Application.WorkItems.Models;

namespace SmartInbox.API.V1.Authentication.Endpoints;

public static class ReviewerAuthenticationEndpoints
{
    public static IEndpointRouteBuilder MapReviewerAuthenticationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup("/api/v1/reviewer");
        auth.MapGet("/antiforgery", GetAntiforgeryTokenAsync);
        auth.MapPost("/login", (Delegate)LoginAsync).AddEndpointFilter<AntiforgeryValidationFilter>();
        auth.MapPost("/logout", (Delegate)LogoutAsync)
            .RequireAuthorization(ReviewerAuthenticationOptions.PolicyName)
            .AddEndpointFilter<AntiforgeryValidationFilter>();
        auth.MapGet("/session", GetSessionAsync).RequireAuthorization(ReviewerAuthenticationOptions.PolicyName);
        return endpoints;
    }

    private static async Task<IResult> GetAntiforgeryTokenAsync(HttpContext context, IAntiforgery antiforgery)
    {
        var authentication = await context.AuthenticateAsync(ReviewerAuthenticationOptions.SchemeName);
        if (authentication.Succeeded && authentication.Principal is not null)
            context.User = authentication.Principal;
        context.Response.Headers.CacheControl = "no-store";
        var tokens = antiforgery.GetAndStoreTokens(context);
        return Results.Ok(new { requestToken = tokens.RequestToken });
    }

    private static async Task<IResult> LoginAsync(
        ReviewerLoginRequestV1 request,
        HttpContext context,
        IOptions<ReviewerAuthenticationOptions> options)
    {
        var configured = options.Value.Password;
        var supplied = request.Password ?? string.Empty;
        var valid = supplied.Length <= 256 && CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(supplied)),
            SHA256.HashData(Encoding.UTF8.GetBytes(configured)));
        if (!valid)
            return Results.Problem(
                type: "https://smartinbox.local/problems/invalid-reviewer-credentials",
                title: "Reviewer sign-in failed",
                statusCode: StatusCodes.Status401Unauthorized);

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, options.Value.ReviewerId),
            new Claim(ClaimTypes.Name, options.Value.DisplayName),
            new Claim("human-reviewer", "true"),
        ], ReviewerAuthenticationOptions.SchemeName);
        await context.SignInAsync(
            ReviewerAuthenticationOptions.SchemeName,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IsPersistent = false,
                AllowRefresh = false,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1),
            });
        context.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new { reviewerId = options.Value.ReviewerId, displayName = options.Value.DisplayName });
    }

    private static async Task<IResult> LogoutAsync(HttpContext context)
    {
        await context.SignOutAsync(ReviewerAuthenticationOptions.SchemeName);
        context.Response.Headers.CacheControl = "no-store";
        return Results.NoContent();
    }

    private static IResult GetSessionAsync(
        HttpContext context,
        EmailDraftingOptions draftingOptions,
        IOptions<GmailIntegrationOptions> gmailOptions)
    {
        context.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new
        {
            reviewerId = context.User.FindFirstValue(ClaimTypes.NameIdentifier),
            displayName = context.User.Identity?.Name,
            aiDraftingEnabled = draftingOptions.Enabled,
            gmailDraftExportEnabled = gmailOptions.Value.Enabled,
            gmailDraftExportMailboxId = gmailOptions.Value.Enabled ? gmailOptions.Value.MailboxId : null,
        });
    }
}
