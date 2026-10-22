namespace SmartInbox.API.Authentication;

public sealed class ServiceAuthenticationOptions
{
    public const string SectionName = "ServiceAuthentication";
    public string Token { get; set; } = string.Empty;

    public bool AreAuthenticationSecretsDistinct(
        ReviewerAuthenticationOptions reviewer,
        GmailIntegrationOptions gmail,
        OutlookIntegrationOptions outlook)
    {
        var secrets = new List<string> { Token, reviewer.Password };
        if (gmail.Enabled)
            secrets.AddRange([gmail.IngestionToken, gmail.DraftExportToken]);
        if (outlook.Enabled)
            secrets.AddRange([outlook.IngestionToken, outlook.DraftExportToken]);

        return secrets.Distinct(StringComparer.Ordinal).Count() == secrets.Count;
    }
}
