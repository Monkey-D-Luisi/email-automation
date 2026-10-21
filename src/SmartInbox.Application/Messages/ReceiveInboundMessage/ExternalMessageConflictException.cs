namespace SmartInbox.Application.Messages.ReceiveInboundMessage;

public sealed class ExternalMessageConflictException()
    : Exception("The external message ID is already associated with different content.");
