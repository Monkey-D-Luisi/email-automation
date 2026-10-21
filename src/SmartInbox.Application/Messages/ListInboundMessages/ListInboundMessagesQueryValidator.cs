using FluentValidation;

namespace SmartInbox.Application.Messages.ListInboundMessages;

public sealed class ListInboundMessagesQueryValidator : AbstractValidator<ListInboundMessagesQuery>
{
    public ListInboundMessagesQueryValidator()
    {
        RuleFor(query => query.PageNumber).InclusiveBetween(1, 10_000);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
    }
}
