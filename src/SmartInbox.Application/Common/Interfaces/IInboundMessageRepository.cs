using SmartInbox.Domain.Messages;

namespace SmartInbox.Application.Common.Interfaces;

public interface IInboundMessageRepository
{
    Task<AddInboundMessageResult> AddIfAbsentAsync(
        InboundMessage message,
        CancellationToken cancellationToken = default);

    Task<InboundMessage?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<InboundMessagePage> ListAsync(
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);
}

public sealed record AddInboundMessageResult(InboundMessage Message, bool Created);

public sealed record InboundMessagePage(
    IReadOnlyList<InboundMessage> Items,
    int PageNumber,
    int PageSize,
    long TotalCount);
