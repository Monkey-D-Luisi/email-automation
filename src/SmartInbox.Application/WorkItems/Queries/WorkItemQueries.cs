using FluentValidation;
using MediatR;
using SmartInbox.Application.Messages.Ports;
using SmartInbox.Application.WorkItems.Models;
using SmartInbox.Application.WorkItems.Ports;
using SmartInbox.Domain.WorkItems;

namespace SmartInbox.Application.WorkItems.Queries;

public sealed record ListWorkItemsQuery(
    int PageNumber = 1,
    int PageSize = 20,
    WorkItemStatus? Status = null,
    SmartInbox.Domain.Messages.EmailCategory? Category = null,
    SmartInbox.Domain.Messages.EmailPriority? Priority = null,
    bool? RequiresReview = null,
    DateTimeOffset? ReceivedAfter = null,
    DateTimeOffset? ReceivedBefore = null) : IRequest<WorkItemPage>;

public sealed class ListWorkItemsQueryValidator : AbstractValidator<ListWorkItemsQuery>
{
    public ListWorkItemsQueryValidator()
    {
        RuleFor(query => query.PageNumber).InclusiveBetween(1, 10_000);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        RuleFor(query => query.Status).Must(value => value is null || Enum.IsDefined(value.Value));
        RuleFor(query => query.Category).Must(value => value is null || Enum.IsDefined(value.Value));
        RuleFor(query => query.Priority).Must(value => value is null || Enum.IsDefined(value.Value));
        RuleFor(query => query).Must(query => query.ReceivedAfter is null || query.ReceivedBefore is null ||
                                              query.ReceivedAfter <= query.ReceivedBefore)
            .WithMessage("ReceivedAfter must be earlier than ReceivedBefore.");
    }
}

public sealed class ListWorkItemsQueryHandler(IWorkItemRepository repository)
    : IRequestHandler<ListWorkItemsQuery, WorkItemPage>
{
    public Task<WorkItemPage> Handle(ListWorkItemsQuery query, CancellationToken cancellationToken) =>
        repository.ListAsync(new WorkItemListFilter(
            query.PageNumber,
            query.PageSize,
            query.Status,
            query.Category,
            query.Priority,
            query.RequiresReview,
            query.ReceivedAfter?.ToUniversalTime(),
            query.ReceivedBefore?.ToUniversalTime()), cancellationToken);
}

public sealed record GetWorkItemQuery(Guid Id) : IRequest<WorkItemDetails?>;

public sealed class GetWorkItemQueryValidator : AbstractValidator<GetWorkItemQuery>
{
    public GetWorkItemQueryValidator() => RuleFor(query => query.Id).NotEmpty();
}

public sealed class GetWorkItemQueryHandler(
    IWorkItemRepository workItems,
    IInboundMessageRepository messages) : IRequestHandler<GetWorkItemQuery, WorkItemDetails?>
{
    public async Task<WorkItemDetails?> Handle(GetWorkItemQuery query, CancellationToken cancellationToken)
    {
        var item = await workItems.GetByIdAsync(query.Id, cancellationToken);
        if (item is null)
            return null;
        var message = await messages.GetByIdAsync(item.SourceMessageId, cancellationToken);
        return message is null ? null : new WorkItemDetails(item, message);
    }
}

public sealed record GetWorkItemHistoryQuery(Guid Id) : IRequest<IReadOnlyList<WorkItemAuditEntry>?>;

public sealed class GetWorkItemHistoryQueryValidator : AbstractValidator<GetWorkItemHistoryQuery>
{
    public GetWorkItemHistoryQueryValidator() => RuleFor(query => query.Id).NotEmpty();
}

public sealed class GetWorkItemHistoryQueryHandler(IWorkItemRepository repository)
    : IRequestHandler<GetWorkItemHistoryQuery, IReadOnlyList<WorkItemAuditEntry>?>
{
    public Task<IReadOnlyList<WorkItemAuditEntry>?> Handle(GetWorkItemHistoryQuery query, CancellationToken cancellationToken) =>
        repository.GetHistoryAsync(query.Id, cancellationToken);
}
