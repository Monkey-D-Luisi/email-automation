using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace SmartInbox.API.Authentication;

public sealed class GmailIntegrationAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<GmailIntegrationOptions> integrationOptions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(schemeOptions, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var values) || values.Count != 1)
            return Task.FromResult(AuthenticateResult.NoResult());

        var authorization = values[0];
        if (authorization is null || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.NoResult());

        var options = integrationOptions.Value;
        var (token, actor, permission) = Scheme.Name switch
        {
            GmailIntegrationOptions.IngestionSchemeName => (options.IngestionToken, "gmail-ingestion", "gmail-ingestion"),
            GmailIntegrationOptions.DraftExportSchemeName => (options.DraftExportToken, "gmail-n8n", "gmail-draft-export"),
            _ => (string.Empty, string.Empty, string.Empty),
        };
        if (!options.Enabled || string.IsNullOrWhiteSpace(token))
            return Task.FromResult(AuthenticateResult.Fail("Gmail integration is disabled or not configured."));

        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(authorization[7..].Trim()));
        var configuredHash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        if (!CryptographicOperations.FixedTimeEquals(suppliedHash, configuredHash))
            return Task.FromResult(AuthenticateResult.Fail("Invalid Gmail integration token."));

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, actor),
            new Claim("gmail-integration", permission),
        ], Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
