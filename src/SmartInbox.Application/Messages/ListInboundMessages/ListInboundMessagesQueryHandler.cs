using MediatR;
using SmartInbox.Application.Common.Interfaces;

namespace SmartInbox.Application.Messages.ListInboundMessages;

public sealed class ListInboundMessagesQueryHandler(IInboundMessageRepository repository)
    : IRequestHandler<ListInboundMessagesQuery, InboundMessagePage>
{
    public Task<InboundMessagePage> Handle(
        ListInboundMessagesQuery query,
        CancellationToken cancellationToken) =>
        repository.ListAsync(query.PageNumber, query.PageSize, cancellationToken);
}
