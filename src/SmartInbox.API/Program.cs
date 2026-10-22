using System.Text.Json.Serialization;
using SmartInbox.API.Authentication;
using SmartInbox.API.Errors;
using SmartInbox.API.Health;
using SmartInbox.API.V1.Authentication.Endpoints;
using SmartInbox.API.V1.Gmail.Endpoints;
using SmartInbox.API.V1.Messages.Endpoints;
using SmartInbox.API.V1.Outlook.Endpoints;
using SmartInbox.API.V1.WorkItems.Endpoints;
using Microsoft.AspNetCore.Authentication.Cookies;
using SmartInbox.Application;
using SmartInbox.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var configuredReviewerOptions = builder.Configuration.GetSection(ReviewerAuthenticationOptions.SectionName).Get<ReviewerAuthenticationOptions>() ?? new();
var configuredGmailOptions = builder.Configuration.GetSection(GmailIntegrationOptions.SectionName).Get<GmailIntegrationOptions>() ?? new();
var configuredOutlookOptions = builder.Configuration.GetSection(OutlookIntegrationOptions.SectionName).Get<OutlookIntegrationOptions>() ?? new();

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
                         !options.Token.Contains("example", StringComparison.OrdinalIgnoreCase) &&
                         options.AreAuthenticationSecretsDistinct(
                             configuredReviewerOptions, configuredGmailOptions, configuredOutlookOptions),
        "Service authentication, reviewer, and enabled email integration secrets must be distinct; service tokens must contain at least 32 characters.")
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
builder.Services
    .AddOptions<GmailIntegrationOptions>()
    .Bind(builder.Configuration.GetSection(GmailIntegrationOptions.SectionName))
    .Validate(options => !options.Enabled ||
        (!string.IsNullOrWhiteSpace(options.SourceId) && options.SourceId.Length <= 64 &&
         options.SourceId.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-') &&
         !string.IsNullOrWhiteSpace(options.MailboxId) && options.MailboxId.Length <= 256 &&
         options.IngestionToken.Length >= 32 && options.DraftExportToken.Length >= 32 &&
         options.ClaimLeaseSeconds is >= 30 and <= 3_600),
        "GmailIntegration requires a source, mailbox ID, service tokens of at least 32 characters, and a claim lease between 30 and 3600 seconds when enabled.")
    .ValidateOnStart();
builder.Services
    .AddOptions<OutlookIntegrationOptions>()
    .Bind(builder.Configuration.GetSection(OutlookIntegrationOptions.SectionName))
    .Validate(options => !options.Enabled ||
        (!string.IsNullOrWhiteSpace(options.SourceId) && options.SourceId.Length <= 64 &&
         options.SourceId.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-') &&
         !string.IsNullOrWhiteSpace(options.MailboxId) && options.MailboxId.Length <= 256 &&
         options.IngestionToken.Length >= 32 && options.DraftExportToken.Length >= 32 &&
         options.HasDistinctSourceIdFrom(configuredGmailOptions) &&
         options.ClaimLeaseSeconds is >= 30 and <= 3_600),
        "OutlookIntegration requires a source and mailbox ID, a distinct provider source ID, unique service credentials, and a claim lease between 30 and 3600 seconds when enabled.")
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
    })
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, GmailIntegrationAuthenticationHandler>(
        GmailIntegrationOptions.IngestionSchemeName, _ => { })
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, GmailIntegrationAuthenticationHandler>(
        GmailIntegrationOptions.DraftExportSchemeName, _ => { });
builder.Services.AddAuthentication()
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, GmailIntegrationAuthenticationHandler>(
        OutlookIntegrationOptions.IngestionSchemeName, _ => { })
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, GmailIntegrationAuthenticationHandler>(
        OutlookIntegrationOptions.DraftExportSchemeName, _ => { });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(ReviewerAuthenticationOptions.PolicyName, policy =>
    {
        policy.AddAuthenticationSchemes(ReviewerAuthenticationOptions.SchemeName);
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("human-reviewer", "true");
        policy.RequireClaim(System.Security.Claims.ClaimTypes.NameIdentifier);
    });
    options.AddPolicy(GmailIntegrationOptions.IngestionPolicyName, policy =>
    {
        policy.AddAuthenticationSchemes(GmailIntegrationOptions.IngestionSchemeName);
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("gmail-integration", "gmail-ingestion");
    });
    options.AddPolicy(GmailIntegrationOptions.DraftExportPolicyName, policy =>
    {
        policy.AddAuthenticationSchemes(GmailIntegrationOptions.DraftExportSchemeName);
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("gmail-integration", "gmail-draft-export");
    });
    options.AddPolicy(OutlookIntegrationOptions.IngestionPolicyName, policy =>
    {
        policy.AddAuthenticationSchemes(OutlookIntegrationOptions.IngestionSchemeName);
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("outlook-integration", "outlook-ingestion");
    });
    options.AddPolicy(OutlookIntegrationOptions.DraftExportPolicyName, policy =>
    {
        policy.AddAuthenticationSchemes(OutlookIntegrationOptions.DraftExportSchemeName);
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("outlook-integration", "outlook-draft-export");
    });
});
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
app.MapGmailIntegrationEndpoints();
app.MapOutlookIntegrationEndpoints();
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
