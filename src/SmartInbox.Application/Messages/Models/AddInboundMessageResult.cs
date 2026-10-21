using SmartInbox.Domain.Messages;

namespace SmartInbox.Application.Messages.Models;

public sealed record AddInboundMessageResult(InboundMessage Message, bool Created);
