using MediatR;
using SmartInbox.Application.Common.Interfaces;

namespace SmartInbox.Application.Messages.ListInboundMessages;

public sealed record ListInboundMessagesQuery(int PageNumber = 1, int PageSize = 20)
    : IRequest<InboundMessagePage>;
