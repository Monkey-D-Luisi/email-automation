using SmartInbox.Domain.Messages;

namespace SmartInbox.Application.Messages.Models;

public sealed record InboundMessageProcessingClaim(InboundMessage Message, Guid LeaseOwner);
