using FluentValidation;

namespace SmartInbox.Application.Messages.GetInboundMessageById;

public sealed class GetInboundMessageByIdQueryValidator : AbstractValidator<GetInboundMessageByIdQuery>
{
    public GetInboundMessageByIdQueryValidator() => RuleFor(query => query.Id).NotEmpty();
}
