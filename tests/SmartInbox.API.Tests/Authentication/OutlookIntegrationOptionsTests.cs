using SmartInbox.API.Authentication;

namespace SmartInbox.API.Tests.Authentication;

public sealed class OutlookIntegrationOptionsTests
{
    [Fact]
    public void Provider_source_ids_must_be_distinct_when_both_integrations_are_enabled()
    {
        var gmail = new GmailIntegrationOptions
        {
            Enabled = true,
            SourceId = "gmail",
        };
        var outlook = new OutlookIntegrationOptions
        {
            Enabled = true,
            SourceId = "outlook",
        };

        Assert.True(outlook.HasDistinctSourceIdFrom(gmail));

        outlook.SourceId = gmail.SourceId;
        Assert.False(outlook.HasDistinctSourceIdFrom(gmail));

        outlook.SourceId = gmail.SourceId.ToUpperInvariant();
        Assert.False(outlook.HasDistinctSourceIdFrom(gmail));
    }

    [Fact]
    public void Provider_isolation_is_not_required_when_either_integration_is_disabled()
    {
        var gmail = new GmailIntegrationOptions { Enabled = true, SourceId = "shared" };
        var outlook = new OutlookIntegrationOptions { Enabled = true, SourceId = "shared" };

        outlook.Enabled = false;

        Assert.True(outlook.HasDistinctSourceIdFrom(gmail));
    }

    [Fact]
    public void Draft_export_is_not_advertised_without_enabled_mailbox_and_service_token()
    {
        var options = new OutlookIntegrationOptions
        {
            Enabled = true,
            MailboxId = "outlook-test@example.test",
        };

        Assert.False(options.DraftExportConfigured);

        options.DraftExportToken = "synthetic-outlook-export-token";
        Assert.True(options.DraftExportConfigured);

        options.MailboxId = " ";
        Assert.False(options.DraftExportConfigured);

        options.MailboxId = "outlook-test@example.test";
        options.Enabled = false;
        Assert.False(options.DraftExportConfigured);
    }
}
