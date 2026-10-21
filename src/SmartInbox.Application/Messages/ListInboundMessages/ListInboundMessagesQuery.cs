using MediatR;
using SmartInbox.Application.Messages.Ports;
using SmartInbox.Application.Messages.Models;

namespace SmartInbox.Application.Messages.ListInboundMessages;

public sealed record ListInboundMessagesQuery(int PageNumber = 1, int PageSize = 20)
    : IRequest<InboundMessagePage>;
