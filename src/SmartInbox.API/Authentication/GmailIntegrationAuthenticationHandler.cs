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
    IOptions<GmailIntegrationOptions> gmailOptions,
    IOptions<OutlookIntegrationOptions> outlookOptions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(schemeOptions, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var values) || values.Count != 1)
            return Task.FromResult(AuthenticateResult.NoResult());

        var authorization = values[0];
        if (authorization is null || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.NoResult());

        var (enabled, token, actor, claimType, permission) = Scheme.Name switch
        {
            GmailIntegrationOptions.IngestionSchemeName => (gmailOptions.Value.Enabled, gmailOptions.Value.IngestionToken, "gmail-ingestion", "gmail-integration", "gmail-ingestion"),
            GmailIntegrationOptions.DraftExportSchemeName => (gmailOptions.Value.Enabled, gmailOptions.Value.DraftExportToken, "gmail-n8n", "gmail-integration", "gmail-draft-export"),
            OutlookIntegrationOptions.IngestionSchemeName => (outlookOptions.Value.Enabled, outlookOptions.Value.IngestionToken, "outlook-ingestion", "outlook-integration", "outlook-ingestion"),
            OutlookIntegrationOptions.DraftExportSchemeName => (outlookOptions.Value.Enabled, outlookOptions.Value.DraftExportToken, "outlook-n8n", "outlook-integration", "outlook-draft-export"),
            _ => (false, string.Empty, string.Empty, string.Empty, string.Empty),
        };
        if (!enabled || string.IsNullOrWhiteSpace(token))
            return Task.FromResult(AuthenticateResult.Fail("Email integration is disabled or not configured."));

        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(authorization[7..].Trim()));
        var configuredHash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        if (!CryptographicOperations.FixedTimeEquals(suppliedHash, configuredHash))
            return Task.FromResult(AuthenticateResult.Fail("Invalid email integration token."));

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, actor),
            new Claim(claimType, permission),
        ], Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
