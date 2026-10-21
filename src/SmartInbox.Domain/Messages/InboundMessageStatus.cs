namespace SmartInbox.Domain.Messages;

public enum InboundMessageStatus
{
    Received,
    Processing,
    Processed,
    Failed,
}
