using SmartInbox.Domain.Messages;

namespace SmartInbox.Application.Messages.Models;

public sealed record InboundMessagePage(
    IReadOnlyList<InboundMessage> Items,
    int PageNumber,
    int PageSize,
    long TotalCount);
