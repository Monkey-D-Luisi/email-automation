using System.Text.Json.Serialization;
using SmartInbox.API.Authentication;
using SmartInbox.API.Errors;
using SmartInbox.API.Health;
using SmartInbox.API.V1.Authentication.Endpoints;
using SmartInbox.API.V1.Messages.Endpoints;
using SmartInbox.API.V1.WorkItems.Endpoints;
using Microsoft.AspNetCore.Authentication.Cookies;
using SmartInbox.Application;
using SmartInbox.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(allowIntegerValues: false));
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services
    .AddOptions<ServiceAuthenticationOptions>()
    .Bind(builder.Configuration.GetSection(ServiceAuthenticationOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.Token) &&
                         options.Token.Length >= 32 &&
                         !options.Token.Contains("replace", StringComparison.OrdinalIgnoreCase) &&
                         !options.Token.Contains("example", StringComparison.OrdinalIgnoreCase),
        "ServiceAuthentication:Token must be a configured service secret of at least 32 characters.")
    .ValidateOnStart();
builder.Services
    .AddAuthentication(ServiceTokenAuthenticationHandler.SchemeName)
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ServiceTokenAuthenticationHandler>(
        ServiceTokenAuthenticationHandler.SchemeName,
        _ => { });
builder.Services
    .AddOptions<ReviewerAuthenticationOptions>()
    .Bind(builder.Configuration.GetSection(ReviewerAuthenticationOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.ReviewerId) && options.ReviewerId.Length <= 128,
        "ReviewAuthentication:ReviewerId must be configured and no longer than 128 characters.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.DisplayName) && options.DisplayName.Length <= 128,
        "ReviewAuthentication:DisplayName must be configured and no longer than 128 characters.")
    .Validate(options => options.Password.Length >= 32 &&
                         !options.Password.Contains("replace", StringComparison.OrdinalIgnoreCase) &&
                         !options.Password.Contains("example", StringComparison.OrdinalIgnoreCase),
        "ReviewAuthentication:Password must be a local secret of at least 32 characters.")
    .ValidateOnStart();
builder.Services.AddAuthentication()
    .AddCookie(ReviewerAuthenticationOptions.SchemeName, options =>
    {
        options.Cookie.Name = "SmartInbox.Reviewer";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(1);
        options.SlidingExpiration = false;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization(options => options.AddPolicy(ReviewerAuthenticationOptions.PolicyName, policy =>
{
    policy.AddAuthenticationSchemes(ReviewerAuthenticationOptions.SchemeName);
    policy.RequireAuthenticatedUser();
    policy.RequireClaim("human-reviewer", "true");
    policy.RequireClaim(System.Security.Claims.ClaimTypes.NameIdentifier);
}));
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "SmartInbox.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

var app = builder.Build();
app.UseExceptionHandler();
app.UseMiddleware<RequestBodyLimitMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapReviewerAuthenticationEndpoints();
app.MapMessageEndpoints();
app.MapWorkItemEndpoints();
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        await System.Text.Json.JsonSerializer.SerializeAsync(
            context.Response.Body,
            new { status = report.Status.ToString().ToLowerInvariant() },
            cancellationToken: context.RequestAborted);
    },
});

app.Run();

public partial class Program;
