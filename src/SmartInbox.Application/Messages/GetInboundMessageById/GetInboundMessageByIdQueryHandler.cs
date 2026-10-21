using MediatR;
using SmartInbox.Application.Messages.Ports;
using SmartInbox.Domain.Messages;

namespace SmartInbox.Application.Messages.GetInboundMessageById;

public sealed class GetInboundMessageByIdQueryHandler(IInboundMessageRepository repository)
    : IRequestHandler<GetInboundMessageByIdQuery, InboundMessage?>
{
    public Task<InboundMessage?> Handle(
        GetInboundMessageByIdQuery query,
        CancellationToken cancellationToken) =>
        repository.GetByIdAsync(query.Id, cancellationToken);
}
