using FluentValidation;
using MediatR;
using SmartInbox.Application.Messages.Ports;
using SmartInbox.Application.WorkItems.Models;
using SmartInbox.Application.WorkItems.Ports;
using SmartInbox.Domain.Messages;
using SmartInbox.Domain.WorkItems;

namespace SmartInbox.Application.WorkItems.Commands;

public sealed record StartReviewCommand(Guid WorkItemId, int ExpectedVersion, string ReviewerId) : IRequest<int>;

public sealed record UpdateReviewCommand(
    Guid WorkItemId,
    int ExpectedVersion,
    EmailCategory Category,
    EmailPriority Priority,
    string? Notes,
    IReadOnlyList<string> MissingInformation,
    string ReviewerId) : IRequest<int>;

public enum ReviewFactAction
{
    Verify,
    Correct,
    Reject,
}

public sealed record ReviewFactCommand(
    Guid WorkItemId,
    Guid FactId,
    int ExpectedVersion,
    ReviewFactAction Action,
    string? Value,
    string ReviewerId) : IRequest<int>;

public sealed record SaveResponseDraftCommand(
    Guid WorkItemId,
    int ExpectedVersion,
    string Subject,
    string Body,
    string ReviewerId) : IRequest<int>;

public sealed record GenerateResponseDraftCommand(Guid WorkItemId, int ExpectedVersion, string ReviewerId) : IRequest<int>;

public sealed record ApproveResponseDraftCommand(
    Guid WorkItemId,
    int ExpectedVersion,
    int Revision,
    string ReviewerId) : IRequest<int>;

public sealed record RejectResponseDraftCommand(
    Guid WorkItemId,
    int ExpectedVersion,
    int Revision,
    string? Feedback,
    string ReviewerId) : IRequest<int>;

public sealed record ResolveWorkItemCommand(Guid WorkItemId, int ExpectedVersion, string ReviewerId) : IRequest<int>;

public sealed class StartReviewCommandValidator : AbstractValidator<StartReviewCommand>
{
    public StartReviewCommandValidator()
    {
        RuleFor(command => command.WorkItemId).NotEmpty();
        RuleFor(command => command.ExpectedVersion).GreaterThanOrEqualTo(0);
        RuleFor(command => command.ReviewerId).NotEmpty().MaximumLength(128);
    }
}

public sealed class UpdateReviewCommandValidator : AbstractValidator<UpdateReviewCommand>
{
    public UpdateReviewCommandValidator()
    {
        RuleFor(command => command.WorkItemId).NotEmpty();
        RuleFor(command => command.ExpectedVersion).GreaterThanOrEqualTo(0);
        RuleFor(command => command.Category).IsInEnum();
        RuleFor(command => command.Priority).IsInEnum();
        RuleFor(command => command.Notes).MaximumLength(WorkItem.MaximumNotesLength);
        RuleFor(command => command.MissingInformation).Cascade(CascadeMode.Stop).NotNull()
            .Must(items => items is { Count: <= WorkItem.MaximumMissingInformationItems });
        RuleForEach(command => command.MissingInformation).NotEmpty().MaximumLength(WorkItem.MaximumMissingInformationLength);
        RuleFor(command => command.ReviewerId).NotEmpty().MaximumLength(128);
    }
}

public sealed class ReviewFactCommandValidator : AbstractValidator<ReviewFactCommand>
{
    public ReviewFactCommandValidator()
    {
        RuleFor(command => command.WorkItemId).NotEmpty();
        RuleFor(command => command.FactId).NotEmpty();
        RuleFor(command => command.ExpectedVersion).GreaterThanOrEqualTo(0);
        RuleFor(command => command.Action).IsInEnum();
        RuleFor(command => command.ReviewerId).NotEmpty().MaximumLength(128);
        RuleFor(command => command.Value)
            .NotEmpty().MaximumLength(300)
            .When(command => command.Action == ReviewFactAction.Correct);
    }
}

public sealed class SaveResponseDraftCommandValidator : AbstractValidator<SaveResponseDraftCommand>
{
    public SaveResponseDraftCommandValidator()
    {
        RuleFor(command => command.WorkItemId).NotEmpty();
        RuleFor(command => command.ExpectedVersion).GreaterThanOrEqualTo(0);
        RuleFor(command => command.Subject).NotEmpty().MaximumLength(998);
        RuleFor(command => command.Body).NotEmpty().MaximumLength(20_000);
        RuleFor(command => command.ReviewerId).NotEmpty().MaximumLength(128);
    }
}

public sealed class GenerateResponseDraftCommandValidator : AbstractValidator<GenerateResponseDraftCommand>
{
    public GenerateResponseDraftCommandValidator()
    {
        RuleFor(command => command.WorkItemId).NotEmpty();
        RuleFor(command => command.ExpectedVersion).GreaterThanOrEqualTo(0);
        RuleFor(command => command.ReviewerId).NotEmpty().MaximumLength(128);
    }
}

public sealed class DraftDecisionCommandValidator : AbstractValidator<ApproveResponseDraftCommand>
{
    public DraftDecisionCommandValidator()
    {
        RuleFor(command => command.WorkItemId).NotEmpty();
        RuleFor(command => command.ExpectedVersion).GreaterThanOrEqualTo(0);
        RuleFor(command => command.Revision).GreaterThan(0);
        RuleFor(command => command.ReviewerId).NotEmpty().MaximumLength(128);
    }
}

public sealed class RejectResponseDraftCommandValidator : AbstractValidator<RejectResponseDraftCommand>
{
    public RejectResponseDraftCommandValidator()
    {
        RuleFor(command => command.WorkItemId).NotEmpty();
        RuleFor(command => command.ExpectedVersion).GreaterThanOrEqualTo(0);
        RuleFor(command => command.Revision).GreaterThan(0);
        RuleFor(command => command.Feedback).MaximumLength(500);
        RuleFor(command => command.ReviewerId).NotEmpty().MaximumLength(128);
    }
}

public sealed class ResolveWorkItemCommandValidator : AbstractValidator<ResolveWorkItemCommand>
{
    public ResolveWorkItemCommandValidator()
    {
        RuleFor(command => command.WorkItemId).NotEmpty();
        RuleFor(command => command.ExpectedVersion).GreaterThanOrEqualTo(0);
        RuleFor(command => command.ReviewerId).NotEmpty().MaximumLength(128);
    }
}

public sealed class WorkItemCommandHandlers(
    IWorkItemRepository workItems,
    IInboundMessageRepository messages,
    IResponseDraftProposer draftProposer,
    EmailDraftingOptions draftingOptions,
    TimeProvider timeProvider) :
    IRequestHandler<StartReviewCommand, int>,
    IRequestHandler<UpdateReviewCommand, int>,
    IRequestHandler<ReviewFactCommand, int>,
    IRequestHandler<SaveResponseDraftCommand, int>,
    IRequestHandler<GenerateResponseDraftCommand, int>,
    IRequestHandler<ApproveResponseDraftCommand, int>,
    IRequestHandler<RejectResponseDraftCommand, int>,
    IRequestHandler<ResolveWorkItemCommand, int>
{
    public Task<int> Handle(StartReviewCommand command, CancellationToken cancellationToken) =>
        MutateAsync(command.WorkItemId, command.ExpectedVersion, item =>
            item.StartReview(command.ReviewerId, timeProvider.GetUtcNow()), cancellationToken);

    public Task<int> Handle(UpdateReviewCommand command, CancellationToken cancellationToken) =>
        MutateAsync(command.WorkItemId, command.ExpectedVersion, item => item.UpdateReview(
            command.Category,
            command.Priority,
            command.Notes,
            command.MissingInformation,
            command.ReviewerId,
            timeProvider.GetUtcNow()), cancellationToken);

    public Task<int> Handle(ReviewFactCommand command, CancellationToken cancellationToken) =>
        MutateAsync(command.WorkItemId, command.ExpectedVersion, item =>
        {
            var now = timeProvider.GetUtcNow();
            switch (command.Action)
            {
                case ReviewFactAction.Verify:
                    item.VerifyFact(command.FactId, command.ReviewerId, now);
                    break;
                case ReviewFactAction.Correct:
                    item.CorrectFact(command.FactId, command.Value!, command.ReviewerId, now);
                    break;
                case ReviewFactAction.Reject:
                    item.RejectFact(command.FactId, command.ReviewerId, now);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(command.Action));
            }
        }, cancellationToken);

    public Task<int> Handle(SaveResponseDraftCommand command, CancellationToken cancellationToken) =>
        SaveDraftAsync(command.WorkItemId, command.ExpectedVersion, command.Subject, command.Body,
            command.ReviewerId, null, cancellationToken);

    public async Task<int> Handle(GenerateResponseDraftCommand command, CancellationToken cancellationToken)
    {
        if (!draftingOptions.Enabled)
            throw new DraftingDisabledException();

        var item = await GetExpectedAsync(command.WorkItemId, command.ExpectedVersion, cancellationToken);
        Apply(() => item.EnsureEditableBy(command.ReviewerId));
        var message = await messages.GetByIdAsync(item.SourceMessageId, cancellationToken)
            ?? throw new WorkItemNotFoundException();
        var classification = message.Classification ?? throw new WorkItemValidationException(
            "A completed classification is required to propose a response draft.");
        if (message.Subject.Length + message.BodyText.Length > draftingOptions.MaxInputCharacters)
            throw new WorkItemValidationException("The email exceeds the configured AI drafting input limit.");

        var context = new DraftProposalContext(
            message.Subject,
            message.BodyText,
            classification.Language,
            classification.Category,
            item.EffectiveCategory,
            item.EffectivePriority,
            item.Facts.Where(fact => fact.Status == ReviewedFactStatus.Verified)
                .Select(fact => new EmailFact(fact.Type, fact.Value)).ToArray(),
            item.MissingInformation,
            item.Facts.Any(fact => fact.Status == ReviewedFactStatus.Unverified));
        DraftProposalResult proposal;
        try
        {
            proposal = await draftProposer.ProposeAsync(context, cancellationToken);
        }
        catch (DraftProposalException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new DraftProposalException("The response draft could not be generated.", exception);
        }
        if (string.IsNullOrWhiteSpace(proposal.Body) || proposal.Body.Length > 20_000)
            throw new DraftProposalException("The response draft provider returned invalid content.");

        var subject = string.IsNullOrWhiteSpace(message.Subject) ? "Response" : message.Subject;
        return await SaveDraftAsync(
            command.WorkItemId,
            command.ExpectedVersion,
            subject,
            proposal.Body,
            command.ReviewerId,
            proposal.Metrics,
            cancellationToken);
    }

    public Task<int> Handle(ApproveResponseDraftCommand command, CancellationToken cancellationToken) =>
        MutateAsync(command.WorkItemId, command.ExpectedVersion, item =>
            item.ApproveDraft(command.Revision, command.ReviewerId, timeProvider.GetUtcNow()), cancellationToken);

    public Task<int> Handle(RejectResponseDraftCommand command, CancellationToken cancellationToken) =>
        MutateAsync(command.WorkItemId, command.ExpectedVersion, item =>
            item.RejectDraft(command.Revision, command.Feedback, command.ReviewerId, timeProvider.GetUtcNow()), cancellationToken);

    public Task<int> Handle(ResolveWorkItemCommand command, CancellationToken cancellationToken) =>
        MutateAsync(command.WorkItemId, command.ExpectedVersion, item =>
            item.Resolve(command.ReviewerId, timeProvider.GetUtcNow()), cancellationToken);

    private async Task<int> SaveDraftAsync(
        Guid id,
        int expectedVersion,
        string subject,
        string body,
        string reviewerId,
        EmailAnalysisMetrics? metrics,
        CancellationToken cancellationToken)
    {
        var item = await GetExpectedAsync(id, expectedVersion, cancellationToken);
        var message = await messages.GetByIdAsync(item.SourceMessageId, cancellationToken)
            ?? throw new WorkItemNotFoundException();
        Apply(() => item.SaveDraft(message.SenderEmail ?? string.Empty, subject, body, reviewerId,
            timeProvider.GetUtcNow(), metrics));
        await workItems.SaveAsync(item, cancellationToken);
        return item.Version;
    }

    private async Task<int> MutateAsync(
        Guid id,
        int expectedVersion,
        Action<WorkItem> mutation,
        CancellationToken cancellationToken)
    {
        var item = await GetExpectedAsync(id, expectedVersion, cancellationToken);
        var version = item.Version;
        Apply(() => mutation(item));
        if (item.Version == version)
            return version;
        await workItems.SaveAsync(item, cancellationToken);
        return item.Version;
    }

    private async Task<WorkItem> GetExpectedAsync(Guid id, int expectedVersion, CancellationToken cancellationToken)
    {
        var item = await workItems.GetByIdAsync(id, cancellationToken) ?? throw new WorkItemNotFoundException();
        if (item.Version != expectedVersion)
            throw new WorkItemConcurrencyException();
        return item;
    }

    private static void Apply(Action mutation)
    {
        try
        {
            mutation();
        }
        catch (KeyNotFoundException)
        {
            throw new WorkItemFactNotFoundException();
        }
        catch (InvalidOperationException exception)
        {
            throw new WorkItemStateConflictException(exception.Message, exception);
        }
        catch (ArgumentException exception)
        {
            throw new WorkItemValidationException(exception.Message, exception);
        }
    }
}
