using FluentValidation;
using MediatR;
using SmartInbox.Application.WorkItems.Models;
using SmartInbox.Application.WorkItems.Ports;
using SmartInbox.Domain.WorkItems;

namespace SmartInbox.Application.WorkItems.Commands;

public sealed record CreateGmailDraftExportCommand(
    Guid WorkItemId,
    int ExpectedVersion,
    int Revision,
    string DestinationMailboxId,
    string RecipientEmail,
    string ReviewerId) : IRequest<GmailDraftExportRequestResult>;

public sealed record ListGmailDraftExportsQuery(Guid WorkItemId) : IRequest<IReadOnlyList<GmailDraftExport>?>;

public sealed record ClaimGmailDraftExportCommand(string DestinationMailboxId, TimeSpan LeaseDuration) : IRequest<GmailDraftExport?>;

public sealed record CompleteGmailDraftExportCommand(
    Guid ExportId,
    Guid AttemptId,
    string GmailDraftId) : IRequest<GmailDraftExportTransition>;

public sealed record FailGmailDraftExportCommand(
    Guid ExportId,
    Guid AttemptId,
    GmailDraftExportFailureKind FailureKind) : IRequest<GmailDraftExportTransition>;

public sealed record RetryGmailDraftExportCommand(
    Guid WorkItemId,
    Guid ExportId,
    int ExpectedVersion,
    string DestinationMailboxId,
    bool ConfirmedNoDraftExists,
    string ReviewerId) : IRequest<GmailDraftExport>;

public sealed record ReconcileGmailDraftExportCommand(
    Guid WorkItemId,
    Guid ExportId,
    int ExpectedVersion,
    string DestinationMailboxId,
    string GmailDraftId,
    string ReviewerId) : IRequest<GmailDraftExport>;

public sealed record GmailDraftExportRequestResult(GmailDraftExport Export, bool Created);

public sealed class CreateGmailDraftExportCommandValidator : AbstractValidator<CreateGmailDraftExportCommand>
{
    public CreateGmailDraftExportCommandValidator()
    {
        RuleFor(command => command.WorkItemId).NotEmpty();
        RuleFor(command => command.ExpectedVersion).GreaterThanOrEqualTo(0);
        RuleFor(command => command.Revision).GreaterThan(0);
        RuleFor(command => command.DestinationMailboxId).NotEmpty().MaximumLength(256);
        RuleFor(command => command.RecipientEmail).NotEmpty().MaximumLength(320).EmailAddress();
        RuleFor(command => command.ReviewerId).NotEmpty().MaximumLength(128);
    }
}

public sealed class ListGmailDraftExportsQueryValidator : AbstractValidator<ListGmailDraftExportsQuery>
{
    public ListGmailDraftExportsQueryValidator() => RuleFor(query => query.WorkItemId).NotEmpty();
}

public sealed class ClaimGmailDraftExportCommandValidator : AbstractValidator<ClaimGmailDraftExportCommand>
{
    public ClaimGmailDraftExportCommandValidator() => RuleFor(command => command.LeaseDuration)
        .Must(duration => duration > TimeSpan.Zero && duration <= TimeSpan.FromHours(1));
}

public sealed class CompleteGmailDraftExportCommandValidator : AbstractValidator<CompleteGmailDraftExportCommand>
{
    public CompleteGmailDraftExportCommandValidator()
    {
        RuleFor(command => command.ExportId).NotEmpty();
        RuleFor(command => command.AttemptId).NotEmpty();
        RuleFor(command => command.GmailDraftId).NotEmpty().MaximumLength(512);
    }
}

public sealed class FailGmailDraftExportCommandValidator : AbstractValidator<FailGmailDraftExportCommand>
{
    public FailGmailDraftExportCommandValidator()
    {
        RuleFor(command => command.ExportId).NotEmpty();
        RuleFor(command => command.AttemptId).NotEmpty();
        RuleFor(command => command.FailureKind).IsInEnum();
    }
}

public sealed class RetryGmailDraftExportCommandValidator : AbstractValidator<RetryGmailDraftExportCommand>
{
    public RetryGmailDraftExportCommandValidator()
    {
        RuleFor(command => command.WorkItemId).NotEmpty();
        RuleFor(command => command.ExportId).NotEmpty();
        RuleFor(command => command.ExpectedVersion).GreaterThanOrEqualTo(0);
        RuleFor(command => command.DestinationMailboxId).NotEmpty().MaximumLength(256);
        RuleFor(command => command.ConfirmedNoDraftExists).Equal(true);
        RuleFor(command => command.ReviewerId).NotEmpty().MaximumLength(128);
    }
}

public sealed class ReconcileGmailDraftExportCommandValidator : AbstractValidator<ReconcileGmailDraftExportCommand>
{
    public ReconcileGmailDraftExportCommandValidator()
    {
        RuleFor(command => command.WorkItemId).NotEmpty();
        RuleFor(command => command.ExportId).NotEmpty();
        RuleFor(command => command.ExpectedVersion).GreaterThanOrEqualTo(0);
        RuleFor(command => command.DestinationMailboxId).NotEmpty().MaximumLength(256);
        RuleFor(command => command.GmailDraftId).NotEmpty().MaximumLength(512);
        RuleFor(command => command.ReviewerId).NotEmpty().MaximumLength(128);
    }
}

public sealed class GmailDraftExportCommandHandlers(
    IWorkItemRepository workItems,
    IGmailDraftExportRepository exports,
    TimeProvider timeProvider) :
    IRequestHandler<CreateGmailDraftExportCommand, GmailDraftExportRequestResult>,
    IRequestHandler<ListGmailDraftExportsQuery, IReadOnlyList<GmailDraftExport>?>,
    IRequestHandler<ClaimGmailDraftExportCommand, GmailDraftExport?>,
    IRequestHandler<CompleteGmailDraftExportCommand, GmailDraftExportTransition>,
    IRequestHandler<FailGmailDraftExportCommand, GmailDraftExportTransition>,
    IRequestHandler<RetryGmailDraftExportCommand, GmailDraftExport>,
    IRequestHandler<ReconcileGmailDraftExportCommand, GmailDraftExport>
{
    public async Task<GmailDraftExportRequestResult> Handle(CreateGmailDraftExportCommand command, CancellationToken cancellationToken)
    {
        var item = await GetExpectedAsync(command.WorkItemId, command.ExpectedVersion, cancellationToken);
        if (item.Draft?.CurrentRevision.Revision != command.Revision)
            throw new WorkItemStateConflictException("Only the current response draft revision can be exported.");

        var export = Apply(() => GmailDraftExport.Create(
            item, command.DestinationMailboxId, command.RecipientEmail, command.ReviewerId, timeProvider.GetUtcNow()));
        var saved = await exports.CreateOrGetAsync(export, item.Version, cancellationToken);
        return new GmailDraftExportRequestResult(saved.Export, saved.Created);
    }

    public async Task<IReadOnlyList<GmailDraftExport>?> Handle(ListGmailDraftExportsQuery query, CancellationToken cancellationToken)
    {
        if (await workItems.GetByIdAsync(query.WorkItemId, cancellationToken) is null)
            return null;
        return await exports.ListByWorkItemAsync(query.WorkItemId, cancellationToken);
    }

    public Task<GmailDraftExport?> Handle(ClaimGmailDraftExportCommand command, CancellationToken cancellationToken) =>
        exports.ClaimPendingAsync(command.DestinationMailboxId, command.LeaseDuration, timeProvider.GetUtcNow(), cancellationToken);

    public Task<GmailDraftExportTransition> Handle(CompleteGmailDraftExportCommand command, CancellationToken cancellationToken) =>
        exports.CompleteAsync(command.ExportId, command.AttemptId, command.GmailDraftId, timeProvider.GetUtcNow(), cancellationToken);

    public Task<GmailDraftExportTransition> Handle(FailGmailDraftExportCommand command, CancellationToken cancellationToken) =>
        exports.FailAsync(command.ExportId, command.AttemptId, command.FailureKind, timeProvider.GetUtcNow(), cancellationToken);

    public async Task<GmailDraftExport> Handle(RetryGmailDraftExportCommand command, CancellationToken cancellationToken)
    {
        var item = await GetExpectedAsync(command.WorkItemId, command.ExpectedVersion, cancellationToken);
        Apply(() => item.EnsureEditableBy(command.ReviewerId));
        return await exports.RetryAfterManualCheckAsync(
            command.WorkItemId, command.ExportId, command.ExpectedVersion, command.DestinationMailboxId, command.ReviewerId,
            timeProvider.GetUtcNow(), cancellationToken) ?? throw new WorkItemNotFoundException();
    }

    public async Task<GmailDraftExport> Handle(ReconcileGmailDraftExportCommand command, CancellationToken cancellationToken)
    {
        var item = await GetExpectedAsync(command.WorkItemId, command.ExpectedVersion, cancellationToken);
        Apply(() => item.EnsureEditableBy(command.ReviewerId));
        return await exports.ReconcileAsync(
            command.WorkItemId, command.ExportId, command.ExpectedVersion, command.DestinationMailboxId, command.ReviewerId,
            command.GmailDraftId, timeProvider.GetUtcNow(), cancellationToken) ?? throw new WorkItemNotFoundException();
    }

    private async Task<SmartInbox.Domain.WorkItems.WorkItem> GetExpectedAsync(
        Guid workItemId,
        int expectedVersion,
        CancellationToken cancellationToken)
    {
        var item = await workItems.GetByIdAsync(workItemId, cancellationToken) ?? throw new WorkItemNotFoundException();
        if (item.Version != expectedVersion)
            throw new WorkItemConcurrencyException();
        return item;
    }

    private static T Apply<T>(Func<T> action)
    {
        try
        {
            return action();
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

    private static void Apply(Action action)
    {
        try
        {
            action();
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
