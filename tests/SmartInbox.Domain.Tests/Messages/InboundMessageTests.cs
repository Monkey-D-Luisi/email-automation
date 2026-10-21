using SmartInbox.Domain.Messages;

namespace SmartInbox.Domain.Tests.Messages;

public sealed class InboundMessageTests
{
    private static readonly DateTimeOffset ImportedAt = new(2026, 10, 21, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Receive_sets_received_state_normalizes_only_semantic_fields_and_uses_utc()
    {
        var receivedAt = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.FromHours(2));
        var message = InboundMessage.Receive(
            "  Mail.Provider  ", "Mailbox-A", "External-ID-A", " Thread-ID-A ", " sender@example.test ", " Sender ",
            " Subject ", " body ", receivedAt, ImportedAt,
            [new AttachmentMetadata("Attachment-A", " report.pdf ", " Application/PDF ", 42)]);

        Assert.NotEqual(Guid.Empty, message.Id);
        Assert.Equal("mail.provider", message.Source);
        Assert.Equal("Mailbox-A", message.MailboxId);
        Assert.Equal("External-ID-A", message.ExternalMessageId);
        Assert.Equal(" Thread-ID-A ", message.ExternalThreadId);
        Assert.Equal("sender@example.test", message.SenderEmail);
        Assert.Equal("Sender", message.SenderName);
        Assert.Equal(" Subject ", message.Subject);
        Assert.Equal(" body ", message.BodyText);
        Assert.Equal(receivedAt.ToUniversalTime(), message.ReceivedAt);
        Assert.Equal(ImportedAt, message.ImportedAt);
        Assert.Equal(InboundMessageStatus.Received, message.Status);
        Assert.Equal(64, message.ContentFingerprint.Length);
        Assert.Equal("report.pdf", Assert.Single(message.Attachments).Name);
        Assert.Equal("application/pdf", message.Attachments[0].ContentType);
    }

    [Fact]
    public void Receive_rejects_missing_required_identity_and_overlong_body()
    {
        Assert.Throws<ArgumentException>(() => InboundMessage.Receive(
            " ", "mailbox", "external", null, null, null, "subject", "body", ImportedAt, ImportedAt));
        Assert.Throws<ArgumentException>(() => InboundMessage.Receive(
            "provider", "mailbox", "external", null, null, null, "subject",
            new string('x', InboundMessageLimits.BodyText + 1), ImportedAt, ImportedAt));
    }

    [Fact]
    public void Fingerprint_is_deterministic_and_preserves_case_sensitive_external_ids()
    {
        var first = Create("External-ID");
        var same = Create("External-ID");
        var differentCase = Create("external-id");

        Assert.Equal(first.ContentFingerprint, same.ContentFingerprint);
        Assert.NotEqual(first.ContentFingerprint, differentCase.ContentFingerprint);
    }

    [Fact]
    public void Status_transitions_allow_processing_retry_and_terminal_completion()
    {
        var message = Create();

        Assert.Throws<InvalidOperationException>(() => message.TransitionTo(InboundMessageStatus.Processed));
        message.TransitionTo(InboundMessageStatus.Processing);
        message.TransitionTo(InboundMessageStatus.Failed);
        message.TransitionTo(InboundMessageStatus.Processing);
        message.TransitionTo(InboundMessageStatus.Processed);

        Assert.Throws<InvalidOperationException>(() => message.TransitionTo(InboundMessageStatus.Processing));
    }

    [Fact]
    public void Attachment_metadata_rejects_negative_size()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AttachmentMetadata("attachment", "file.txt", "text/plain", -1));
    }

    private static InboundMessage Create(string externalId = "external-id") => InboundMessage.Receive(
        "provider", "mailbox", externalId, "thread", "sender@example.test", "Sender", "Subject", "Body",
        ImportedAt, ImportedAt);
}
