using SmartInbox.API.Authentication;

namespace SmartInbox.API.Tests.Authentication;

public sealed class ServiceAuthenticationOptionsTests
{
    [Fact]
    public void Enabled_authentication_secrets_must_be_distinct_across_schemes()
    {
        var service = new ServiceAuthenticationOptions { Token = "service-token" };
        var reviewer = new ReviewerAuthenticationOptions { Password = "reviewer-password" };
        var gmail = new GmailIntegrationOptions
        {
            Enabled = true,
            IngestionToken = "gmail-ingestion-token",
            DraftExportToken = "gmail-export-token",
        };
        var outlook = new OutlookIntegrationOptions
        {
            Enabled = true,
            IngestionToken = "outlook-ingestion-token",
            DraftExportToken = "outlook-export-token",
        };

        Assert.True(service.AreAuthenticationSecretsDistinct(reviewer, gmail, outlook));

        reviewer.Password = service.Token;
        Assert.False(service.AreAuthenticationSecretsDistinct(reviewer, gmail, outlook));

        reviewer.Password = "reviewer-password";
        gmail.IngestionToken = reviewer.Password;
        Assert.False(service.AreAuthenticationSecretsDistinct(reviewer, gmail, outlook));

        gmail.IngestionToken = "gmail-ingestion-token";
        gmail.DraftExportToken = gmail.IngestionToken;
        Assert.False(service.AreAuthenticationSecretsDistinct(reviewer, gmail, outlook));

        gmail.DraftExportToken = "gmail-export-token";
        outlook.IngestionToken = service.Token;
        Assert.False(service.AreAuthenticationSecretsDistinct(reviewer, gmail, outlook));

        outlook.IngestionToken = "outlook-ingestion-token";
        outlook.DraftExportToken = gmail.DraftExportToken;
        Assert.False(service.AreAuthenticationSecretsDistinct(reviewer, gmail, outlook));
    }

    [Fact]
    public void Disabled_integration_tokens_do_not_participate_in_secret_uniqueness()
    {
        var service = new ServiceAuthenticationOptions { Token = "service-token" };
        var reviewer = new ReviewerAuthenticationOptions { Password = "reviewer-password" };
        var gmail = new GmailIntegrationOptions
        {
            IngestionToken = service.Token,
            DraftExportToken = service.Token,
        };
        var outlook = new OutlookIntegrationOptions
        {
            IngestionToken = service.Token,
            DraftExportToken = service.Token,
        };

        Assert.True(service.AreAuthenticationSecretsDistinct(reviewer, gmail, outlook));
    }
}
