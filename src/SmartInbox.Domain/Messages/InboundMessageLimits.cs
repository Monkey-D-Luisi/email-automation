namespace SmartInbox.Domain.Messages;

public static class InboundMessageLimits
{
    public const int Source = 64;
    public const int MailboxId = 256;
    public const int ExternalId = 512;
    public const int SenderEmail = 320;
    public const int SenderName = 256;
    public const int Subject = 998;
    public const int BodyText = 40_000;
    public const int AttachmentCount = 10;
    public const int AttachmentName = 255;
    public const int ContentType = 127;
}
