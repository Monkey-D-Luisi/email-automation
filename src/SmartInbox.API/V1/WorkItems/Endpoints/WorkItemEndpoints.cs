using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using SmartInbox.API.Authentication;
using SmartInbox.API.V1.WorkItems.Contracts;
using SmartInbox.Application.WorkItems.Commands;
using SmartInbox.Application.WorkItems.Models;
using SmartInbox.Application.WorkItems.Queries;

namespace SmartInbox.API.V1.WorkItems.Endpoints;

public static class WorkItemEndpoints
{
    public static IEndpointRouteBuilder MapWorkItemEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var workItems = endpoints.MapGroup("/api/v1/work-items")
            .RequireAuthorization(ReviewerAuthenticationOptions.PolicyName)
            .AddEndpointFilter<AntiforgeryValidationFilter>();

        workItems.MapGet("", ListAsync);
        workItems.MapGet("/{id:guid}", GetAsync);
        workItems.MapGet("/{id:guid}/history", GetHistoryAsync);
        workItems.MapPost("/{id:guid}/start-review", StartReviewAsync);
        workItems.MapPatch("/{id:guid}/review", UpdateReviewAsync);
        workItems.MapPost("/{id:guid}/facts/{factId:guid}/verify", VerifyFactAsync);
        workItems.MapPatch("/{id:guid}/facts/{factId:guid}", CorrectFactAsync);
        workItems.MapPost("/{id:guid}/facts/{factId:guid}/reject", RejectFactAsync);
        workItems.MapPost("/{id:guid}/draft", SaveDraftAsync);
        workItems.MapPost("/{id:guid}/draft/generate", GenerateDraftAsync);
        workItems.MapPost("/{id:guid}/draft/approve", ApproveDraftAsync);
        workItems.MapPost("/{id:guid}/draft/reject", RejectDraftAsync);
        workItems.MapPost("/{id:guid}/resolve", ResolveAsync);
        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        [FromQuery] int? pageNumber,
        [FromQuery] int? pageSize,
        [FromQuery] SmartInbox.Domain.WorkItems.WorkItemStatus? status,
        [FromQuery] SmartInbox.Domain.Messages.EmailCategory? category,
        [FromQuery] SmartInbox.Domain.Messages.EmailPriority? priority,
        [FromQuery] bool? requiresReview,
        [FromQuery] DateTimeOffset? receivedAfter,
        [FromQuery] DateTimeOffset? receivedBefore,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var page = await sender.Send(new ListWorkItemsQuery(
            pageNumber ?? 1,
            pageSize ?? 20,
            status,
            category,
            priority,
            requiresReview,
            receivedAfter,
            receivedBefore), cancellationToken);
        return Results.Ok(new WorkItemListResponseV1(
            page.Items.Select(item => new WorkItemListItemResponseV1(
                item.Id,
                item.SourceMessageId,
                item.Subject,
                item.SenderEmail,
                item.SenderName,
                item.ReceivedAt,
                item.Status.ToString(),
                item.Category.ToString(),
                item.Priority.ToString(),
                item.RequiresReview,
                item.AssignedReviewerId,
                item.Version)).ToArray(),
            page.PageNumber,
            page.PageSize,
            page.TotalCount));
    }

    private static async Task<IResult> GetAsync(Guid id, ISender sender, CancellationToken cancellationToken)
    {
        var details = await sender.Send(new GetWorkItemQuery(id), cancellationToken);
        return details is null
            ? NotFound()
            : Results.Ok(ToResponse(details));
    }

    private static async Task<IResult> GetHistoryAsync(Guid id, ISender sender, CancellationToken cancellationToken)
    {
        var entries = await sender.Send(new GetWorkItemHistoryQuery(id), cancellationToken);
        return entries is null
            ? NotFound()
            : Results.Ok(new WorkItemHistoryResponseV1(entries.Select(entry => new WorkItemAuditEntryResponseV1(
                entry.Id,
                entry.WorkItemId,
                entry.ActorId,
                entry.Action.ToString(),
                entry.OccurredAt,
                entry.EntityId,
                entry.Revision,
                entry.Summary)).ToArray()));
    }

    private static async Task<IResult> StartReviewAsync(
        Guid id,
        ExpectedVersionRequestV1 request,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken) =>
        Mutation(await sender.Send(new StartReviewCommand(id, request.ExpectedVersion, ReviewerId(context)), cancellationToken));

    private static async Task<IResult> UpdateReviewAsync(
        Guid id,
        UpdateReviewRequestV1 request,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken) =>
        Mutation(await sender.Send(new UpdateReviewCommand(
            id,
            request.ExpectedVersion,
            request.Category,
            request.Priority,
            request.Notes,
            request.MissingInformation,
            ReviewerId(context)), cancellationToken));

    private static async Task<IResult> VerifyFactAsync(
        Guid id,
        Guid factId,
        ExpectedVersionRequestV1 request,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken) =>
        Mutation(await sender.Send(new ReviewFactCommand(
            id, factId, request.ExpectedVersion, ReviewFactAction.Verify, null, ReviewerId(context)), cancellationToken));

    private static async Task<IResult> CorrectFactAsync(
        Guid id,
        Guid factId,
        CorrectFactRequestV1 request,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken) =>
        Mutation(await sender.Send(new ReviewFactCommand(
            id, factId, request.ExpectedVersion, ReviewFactAction.Correct, request.Value, ReviewerId(context)), cancellationToken));

    private static async Task<IResult> RejectFactAsync(
        Guid id,
        Guid factId,
        ExpectedVersionRequestV1 request,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken) =>
        Mutation(await sender.Send(new ReviewFactCommand(
            id, factId, request.ExpectedVersion, ReviewFactAction.Reject, null, ReviewerId(context)), cancellationToken));

    private static async Task<IResult> SaveDraftAsync(
        Guid id,
        SaveResponseDraftRequestV1 request,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken) =>
        Mutation(await sender.Send(new SaveResponseDraftCommand(
            id, request.ExpectedVersion, request.Subject, request.Body, ReviewerId(context)), cancellationToken));

    private static async Task<IResult> GenerateDraftAsync(
        Guid id,
        ExpectedVersionRequestV1 request,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken) =>
        Mutation(await sender.Send(new GenerateResponseDraftCommand(
            id, request.ExpectedVersion, ReviewerId(context)), cancellationToken));

    private static async Task<IResult> ApproveDraftAsync(
        Guid id,
        ApproveDraftRequestV1 request,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken) =>
        Mutation(await sender.Send(new ApproveResponseDraftCommand(
            id, request.ExpectedVersion, request.Revision, ReviewerId(context)), cancellationToken));

    private static async Task<IResult> RejectDraftAsync(
        Guid id,
        RejectDraftRequestV1 request,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken) =>
        Mutation(await sender.Send(new RejectResponseDraftCommand(
            id, request.ExpectedVersion, request.Revision, request.Feedback, ReviewerId(context)), cancellationToken));

    private static async Task<IResult> ResolveAsync(
        Guid id,
        ExpectedVersionRequestV1 request,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken) =>
        Mutation(await sender.Send(new ResolveWorkItemCommand(
            id, request.ExpectedVersion, ReviewerId(context)), cancellationToken));

    private static string ReviewerId(HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("The authenticated reviewer identity is missing.");

    private static IResult Mutation(int version) => Results.Ok(new WorkItemMutationResponseV1(version));

    private static IResult NotFound() => Results.Problem(
        type: "https://smartinbox.local/problems/work-item-not-found",
        title: "Work item not found",
        statusCode: StatusCodes.Status404NotFound);

    private static WorkItemResponseV1 ToResponse(WorkItemDetails details)
    {
        var item = details.WorkItem;
        var message = details.SourceMessage;
        var original = message.Classification;
        return new WorkItemResponseV1(
            item.Id,
            item.SourceMessageId,
            item.Status.ToString(),
            item.AssignedReviewerId,
            item.EffectiveCategory.ToString(),
            item.EffectivePriority.ToString(),
            item.ReviewerNotes,
            item.MissingInformation,
            item.CreatedAt,
            item.UpdatedAt,
            item.ResolvedAt,
            item.ResolvedBy,
            item.Version,
            message.Subject,
            message.BodyText,
            message.SenderEmail,
            message.SenderName,
            message.ReceivedAt,
            message.ImportedAt,
            message.Status.ToString(),
            message.ProcessingAttemptCount,
            message.FailureKind?.ToString(),
            message.FailureKind is null ? null : "Processing did not complete. Review the failure category and server logs.",
            original is null ? null : new OriginalClassificationResponseV1(
                original.Category.ToString(),
                original.Priority.ToString(),
                original.Summary,
                original.Language,
                original.Facts.Select(fact => new EmailFactResponseV1(fact.Type.ToString(), fact.Value)).ToArray(),
                original.MissingInformation,
                original.Warnings,
                original.UncertaintyReasons,
                original.ReviewReasons.Select(reason => reason.ToString()).ToArray(),
                original.RequiresReview,
                original.ClassifiedAt),
            item.Facts.Select(fact => new ReviewedFactResponseV1(
                fact.Id,
                fact.Type.ToString(),
                fact.OriginalValue,
                fact.Value,
                fact.Status.ToString(),
                fact.ModifiedAt,
                fact.ModifiedBy)).ToArray(),
            item.Draft is null ? null : new ResponseDraftResponseV1(
                item.Draft.Id,
                item.Draft.RecipientEmail,
                item.Draft.CurrentRevision.Revision,
                item.Draft.Revisions.Select(revision => new ResponseDraftRevisionResponseV1(
                    revision.Id,
                    revision.Revision,
                    revision.Subject,
                    revision.Body,
                    revision.Status.ToString(),
                    revision.CreatedAt,
                    revision.CreatedBy,
                    revision.DecidedAt,
                    revision.DecidedBy,
                    revision.Feedback,
                    revision.GenerationMetrics is null ? null : new DraftGenerationMetricsResponseV1(
                        revision.GenerationMetrics.ProviderId,
                        revision.GenerationMetrics.Model,
                        revision.GenerationMetrics.DurationMilliseconds,
                        revision.GenerationMetrics.InputTokens,
                        revision.GenerationMetrics.CachedInputTokens,
                        revision.GenerationMetrics.OutputTokens,
                        revision.GenerationMetrics.EstimatedCostUsd))).ToArray()));
    }
}
