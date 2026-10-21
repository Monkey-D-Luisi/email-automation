using MediatR;
using SmartInbox.Domain.Messages;

namespace SmartInbox.Application.Messages.GetInboundMessageById;

public sealed record GetInboundMessageByIdQuery(Guid Id) : IRequest<InboundMessage?>;
