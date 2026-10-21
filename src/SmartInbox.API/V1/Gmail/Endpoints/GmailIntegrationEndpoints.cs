using System.Text.RegularExpressions;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SmartInbox.API.Authentication;
using SmartInbox.API.V1.Gmail.Contracts;
using SmartInbox.API.V1.Messages.Contracts;
using SmartInbox.Application.Messages.ReceiveInboundMessage;
using SmartInbox.Application.WorkItems.Commands;
using SmartInbox.Domain.WorkItems;

namespace SmartInbox.API.V1.Gmail.Endpoints;

public static partial class GmailIntegrationEndpoints
{
    public static IEndpointRouteBuilder MapGmailIntegrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var gmail = endpoints.MapGroup("/api/v1/integrations/gmail");
        gmail.MapPost("/messages", ReceiveAsync)
            .RequireAuthorization(GmailIntegrationOptions.IngestionPolicyName);

        var exports = gmail.MapGroup("/draft-exports")
            .RequireAuthorization(GmailIntegrationOptions.DraftExportPolicyName);
        exports.MapPost("/claim", ClaimAsync);
        exports.MapPost("/{id:guid}/complete", CompleteAsync);
        exports.MapPost("/{id:guid}/fail", FailAsync);
        return endpoints;
    }

    private static async Task<IResult> ReceiveAsync(
        GmailInboundMessageRequestV1 request,
        HttpContext context,
        ISender sender,
        IOptions<GmailIntegrationOptions> options,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var attachments = request.Attachments?.Select(item => item is null
            ? null
            : new InboundAttachmentInput(
                item.ExternalAttachmentId!, item.Name!, item.ContentType!, item.SizeBytes ?? -1)).ToArray();
        var command = new ReceiveInboundMessageCommand(
            options.Value.SourceId,
            options.Value.MailboxId,
            request.GmailMessageId!,
            request.GmailThreadId,
            request.SenderEmail,
            request.SenderName,
            request.Subject!,
            request.BodyText!,
            request.ReceivedAt ?? default,
            attachments);

        ReceiveInboundMessageResult result;
        try
        {
            result = await sender.Send(command, cancellationToken);
        }
        catch (ExternalMessageConflictException)
        {
            loggerFactory.CreateLogger("GmailIntegration").LogWarning(
                "Gmail message replay conflicted for integration correlation {CorrelationId}.", CorrelationId(context));
            throw;
        }

        loggerFactory.CreateLogger("GmailIntegration").LogInformation(
            "Gmail message {Outcome} for integration correlation {CorrelationId}.",
            result.Created ? "accepted" : "already stored",
            CorrelationId(context));
        var response = new ReceiveInboundMessageResponseV1(result.Id, "Received");
        return result.Created
            ? Results.Accepted($"/api/v1/messages/{result.Id:D}", response)
            : Results.Ok(response);
    }

    private static async Task<IResult> ClaimAsync(ISender sender, IOptions<GmailIntegrationOptions> options, CancellationToken cancellationToken)
    {
        var export = await sender.Send(new ClaimGmailDraftExportCommand(
            options.Value.MailboxId,
            TimeSpan.FromSeconds(options.Value.ClaimLeaseSeconds)), cancellationToken);
        return Results.Ok(new ClaimGmailDraftExportResponseV1(export is null ? null : new GmailDraftExportClaimV1(
            export.Id,
            export.AttemptId!.Value,
            export.WorkItemId,
            export.DraftId,
            export.DraftRevisionId,
            export.ApprovedRevision,
            export.DestinationMailboxId,
            export.RecipientEmail,
            export.Subject,
            export.Body,
            export.ContentFingerprint,
            export.LeaseExpiresAt!.Value)));
    }

    private static async Task<IResult> CompleteAsync(
        Guid id,
        CompleteGmailDraftExportRequestV1 request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var status = await sender.Send(new CompleteGmailDraftExportCommand(id, request.AttemptId, request.GmailDraftId), cancellationToken);
        return CallbackResult(status);
    }

    private static async Task<IResult> FailAsync(
        Guid id,
        FailGmailDraftExportRequestV1 request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var status = await sender.Send(new FailGmailDraftExportCommand(id, request.AttemptId, request.FailureKind), cancellationToken);
        return CallbackResult(status);
    }

    private static IResult CallbackResult(GmailDraftExportTransition status) => status switch
    {
        GmailDraftExportTransition.Applied or GmailDraftExportTransition.Duplicate =>
            Results.Ok(new GmailDraftExportCallbackResponseV1(status.ToString())),
        GmailDraftExportTransition.LeaseExpired => Results.Conflict(new ProblemDetails
        {
            Type = "https://smartinbox.local/problems/gmail-export-lease-expired",
            Title = "Gmail draft export requires reconciliation",
            Status = StatusCodes.Status409Conflict,
        }),
        _ => Results.Conflict(new ProblemDetails
        {
            Type = "https://smartinbox.local/problems/stale-gmail-export-callback",
            Title = "Gmail draft export callback is stale",
            Status = StatusCodes.Status409Conflict,
        }),
    };

    private static string CorrelationId(HttpContext context)
    {
        var value = context.Request.Headers["X-Correlation-ID"].FirstOrDefault();
        return !string.IsNullOrWhiteSpace(value) && value.Length <= 128 && CorrelationIdPattern().IsMatch(value)
            ? value
            : context.TraceIdentifier;
    }

    [GeneratedRegex("^[A-Za-z0-9._:-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex CorrelationIdPattern();
}
