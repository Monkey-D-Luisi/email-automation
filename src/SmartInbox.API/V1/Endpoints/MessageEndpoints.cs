using MediatR;
using Microsoft.AspNetCore.Mvc;
using SmartInbox.API.V1.Contracts;
using SmartInbox.Application.Messages.GetInboundMessageById;
using SmartInbox.Application.Messages.ListInboundMessages;
using SmartInbox.Application.Messages.ReceiveInboundMessage;
using SmartInbox.Domain.Messages;

namespace SmartInbox.API.V1.Endpoints;

public static class MessageEndpoints
{
    public static IEndpointRouteBuilder MapMessageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var messages = endpoints.MapGroup("/api/v1/messages").RequireAuthorization();

        messages.MapPost("", ReceiveAsync);
        messages.MapGet("/{id:guid}", GetByIdAsync).WithName("GetInboundMessageByIdV1");
        messages.MapGet("", ListAsync);
        return endpoints;
    }

    private static async Task<IResult> ReceiveAsync(
        InboundMessageRequestV1 request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var attachments = request.Attachments?.Select(item => item is null
            ? null
            : new InboundAttachmentInput(
                item.ExternalAttachmentId!,
                item.Name!,
                item.ContentType!,
                item.SizeBytes ?? -1)).ToArray();
        var result = await sender.Send(new ReceiveInboundMessageCommand(
            request.Source!,
            request.MailboxId!,
            request.ExternalMessageId!,
            request.ExternalThreadId,
            request.SenderEmail,
            request.SenderName,
            request.Subject!,
            request.BodyText!,
            request.ReceivedAt ?? default,
            attachments), cancellationToken);
        var response = new ReceiveInboundMessageResponseV1(result.Id, "Received");
        return result.Created
            ? Results.Accepted($"/api/v1/messages/{result.Id:D}", response)
            : Results.Ok(response);
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var message = await sender.Send(new GetInboundMessageByIdQuery(id), cancellationToken);
        return message is null
            ? Results.Problem(
                type: "https://smartinbox.local/problems/message-not-found",
                title: "Message not found",
                statusCode: StatusCodes.Status404NotFound)
            : Results.Ok(ToResponse(message));
    }

    private static async Task<IResult> ListAsync(
        [FromQuery] int? pageNumber,
        [FromQuery] int? pageSize,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var page = await sender.Send(new ListInboundMessagesQuery(pageNumber ?? 1, pageSize ?? 20), cancellationToken);
        return Results.Ok(new InboundMessageListResponseV1(
            page.Items.Select(ToResponse).ToArray(),
            page.PageNumber,
            page.PageSize,
            page.TotalCount));
    }

    private static InboundMessageResponseV1 ToResponse(InboundMessage message) => new(
        message.Id,
        message.Source,
        message.MailboxId,
        message.ExternalMessageId,
        message.ExternalThreadId,
        message.SenderEmail,
        message.SenderName,
        message.Subject,
        message.BodyText,
        message.ReceivedAt,
        message.ImportedAt,
        message.Status.ToString(),
        message.Attachments.Select(attachment => new AttachmentMetadataResponseV1(
            attachment.ExternalAttachmentId,
            attachment.Name,
            attachment.ContentType,
            attachment.SizeBytes)).ToArray(),
        message.Classification is null ? null : new EmailClassificationResponseV1(
            message.Classification.Category.ToString(),
            message.Classification.Priority.ToString(),
            message.Classification.Summary,
            message.Classification.Language,
            message.Classification.Facts.Select(fact => new EmailFactResponseV1(fact.Type.ToString(), fact.Value)).ToArray(),
            message.Classification.MissingInformation,
            message.Classification.Warnings,
            message.Classification.UncertaintyReasons,
            message.Classification.ReviewReasons.Select(reason => reason.ToString()).ToArray(),
            message.Classification.RequiresReview,
            message.Classification.ClassifiedAt),
        new EmailProcessingResponseV1(
            message.ProcessingAttemptCount,
            message.FailureKind?.ToString(),
            message.FailureMessage,
            message.ProcessingRetryable,
            message.NextAttemptAt,
            message.AnalysisMetrics is null ? null : new EmailAnalysisMetricsResponseV1(
                message.AnalysisMetrics.ProviderId,
                message.AnalysisMetrics.Model,
                message.AnalysisMetrics.DurationMilliseconds,
                message.AnalysisMetrics.InputTokens,
                message.AnalysisMetrics.CachedInputTokens,
                message.AnalysisMetrics.OutputTokens,
                message.AnalysisMetrics.EstimatedCostUsd)));
}
