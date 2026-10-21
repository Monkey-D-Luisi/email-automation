using System.Text.Json.Serialization;
using SmartInbox.API.Authentication;
using SmartInbox.API.Errors;
using SmartInbox.API.Health;
using SmartInbox.API.V1.Endpoints;
using SmartInbox.Application;
using SmartInbox.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);
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
builder.Services.AddAuthorization();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

var app = builder.Build();
app.UseExceptionHandler();
app.UseMiddleware<RequestBodyLimitMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.MapMessageEndpoints();
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
