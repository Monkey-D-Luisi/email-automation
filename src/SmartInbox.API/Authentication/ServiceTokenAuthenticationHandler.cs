using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace SmartInbox.API.Authentication;

public sealed class ServiceTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<ServiceAuthenticationOptions> serviceOptions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(schemeOptions, logger, encoder)
{
    public const string SchemeName = "ServiceToken";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var values) || values.Count != 1)
            return Task.FromResult(AuthenticateResult.NoResult());

        var authorization = values[0];
        if (authorization is null ||
            !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.NoResult());

        var suppliedToken = authorization[7..].Trim();
        var configuredToken = serviceOptions.Value.Token;
        if (string.IsNullOrEmpty(configuredToken))
            return Task.FromResult(AuthenticateResult.Fail("Service authentication is not configured."));

        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(suppliedToken));
        var configuredHash = SHA256.HashData(Encoding.UTF8.GetBytes(configuredToken));
        if (!CryptographicOperations.FixedTimeEquals(suppliedHash, configuredHash))
            return Task.FromResult(AuthenticateResult.Fail("Invalid service token."));

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "service-client")], SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
