using NSubstitute;
using SmartInbox.Application.Messages.Ports;
using SmartInbox.Application.WorkItems.Commands;
using SmartInbox.Application.WorkItems.Models;
using SmartInbox.Application.WorkItems.Ports;
using SmartInbox.Domain.Messages;
using SmartInbox.Domain.WorkItems;

namespace SmartInbox.Application.Tests.WorkItems;

public sealed class WorkItemCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Stale_work_item_version_is_rejected_before_any_save()
    {
        var workItems = Substitute.For<IWorkItemRepository>();
        var item = WorkItem.Create(Guid.NewGuid(), Classification(), Now);
        item.StartReview("reviewer-1", Now.AddMinutes(1));
        workItems.GetByIdAsync(item.Id, Arg.Any<CancellationToken>()).Returns(item);
        var handler = CreateHandler(workItems);

        await Assert.ThrowsAsync<WorkItemConcurrencyException>(() => handler.Handle(
            new UpdateReviewCommand(item.Id, 0, EmailCategory.SupportRequest, EmailPriority.High, null, [], "reviewer-1"),
            CancellationToken.None));

        await workItems.DidNotReceive().SaveAsync(Arg.Any<WorkItem>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Disabled_ai_drafting_does_not_call_the_provider()
    {
        var proposer = Substitute.For<IResponseDraftProposer>();
        var handler = CreateHandler(Substitute.For<IWorkItemRepository>(), proposer);

        await Assert.ThrowsAsync<DraftingDisabledException>(() => handler.Handle(
            new GenerateResponseDraftCommand(Guid.NewGuid(), 0, "reviewer-1"),
            CancellationToken.None));

        await proposer.DidNotReceive().ProposeAsync(Arg.Any<DraftProposalContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Generated_draft_uses_only_verified_facts_and_stays_unapproved()
    {
        var workItems = Substitute.For<IWorkItemRepository>();
        var messages = Substitute.For<IInboundMessageRepository>();
        var proposer = Substitute.For<IResponseDraftProposer>();
        var classification = Classification(EmailCategory.Unknown);
        var item = WorkItem.Create(Guid.NewGuid(), classification, Now);
        item.StartReview("reviewer-1", Now.AddMinutes(1));
        item.VerifyFact(item.Facts[0].Id, "reviewer-1", Now.AddMinutes(2));
        var message = ProcessedMessage(item.SourceMessageId, classification);
        workItems.GetByIdAsync(item.Id, Arg.Any<CancellationToken>()).Returns(item);
        workItems.SaveAsync(Arg.Any<WorkItem>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        messages.GetByIdAsync(item.SourceMessageId, Arg.Any<CancellationToken>()).Returns(message);
        proposer.ProposeAsync(Arg.Any<DraftProposalContext>(), Arg.Any<CancellationToken>())
            .Returns(new DraftProposalResult("Could you confirm the invoice number?", new EmailAnalysisMetrics("openai", "synthetic-model", 10, 30, null, 8, null)));
        var handler = CreateHandler(workItems, proposer, messages, enabled: true);

        var version = await handler.Handle(
            new GenerateResponseDraftCommand(item.Id, item.Version, "reviewer-1"),
            CancellationToken.None);

        Assert.Equal(item.Version, version);
        Assert.NotNull(item.Draft);
        Assert.Equal(ResponseDraftStatus.Draft, item.Draft.CurrentRevision.Status);
        Assert.Equal("openai", item.Draft.CurrentRevision.GenerationMetrics!.ProviderId);
        Assert.Equal("alex@example.test", item.Draft.RecipientEmail);
        await proposer.Received(1).ProposeAsync(
            Arg.Is<DraftProposalContext>(value => value.OriginalCategory == EmailCategory.Unknown &&
                                                  value.HasUnverifiedFacts && value.VerifiedFacts.Count == 1 &&
                                                  value.VerifiedFacts[0].Value == "INV-2030"),
            Arg.Any<CancellationToken>());
        await workItems.Received(1).SaveAsync(item, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invalid_provider_output_is_not_saved()
    {
        var workItems = Substitute.For<IWorkItemRepository>();
        var messages = Substitute.For<IInboundMessageRepository>();
        var proposer = Substitute.For<IResponseDraftProposer>();
        var classification = Classification();
        var item = WorkItem.Create(Guid.NewGuid(), classification, Now);
        item.StartReview("reviewer-1", Now.AddMinutes(1));
        workItems.GetByIdAsync(item.Id, Arg.Any<CancellationToken>()).Returns(item);
        messages.GetByIdAsync(item.SourceMessageId, Arg.Any<CancellationToken>())
            .Returns(ProcessedMessage(item.SourceMessageId, classification));
        proposer.ProposeAsync(Arg.Any<DraftProposalContext>(), Arg.Any<CancellationToken>())
            .Returns(new DraftProposalResult(new string('x', 20_001), new EmailAnalysisMetrics("openai", "synthetic-model", 1, 1, null, 1, null)));
        var handler = CreateHandler(workItems, proposer, messages, enabled: true);

        await Assert.ThrowsAsync<DraftProposalException>(() => handler.Handle(
            new GenerateResponseDraftCommand(item.Id, item.Version, "reviewer-1"),
            CancellationToken.None));
        await workItems.DidNotReceive().SaveAsync(Arg.Any<WorkItem>(), Arg.Any<CancellationToken>());
    }

    private static WorkItemCommandHandlers CreateHandler(
        IWorkItemRepository workItems,
        IResponseDraftProposer? proposer = null,
        IInboundMessageRepository? messages = null,
        bool enabled = false) => new(
        workItems,
        messages ?? Substitute.For<IInboundMessageRepository>(),
        proposer ?? Substitute.For<IResponseDraftProposer>(),
        new EmailDraftingOptions { Enabled = enabled },
        TimeProvider.System);

    private static InboundMessage ProcessedMessage(Guid id, EmailClassification classification)
    {
        var message = InboundMessage.Receive(
            "synthetic", "mailbox", $"external-{id:N}", null, "alex@example.test", "Alex Synthetic",
            "Invoice question", "Could you review invoice INV-2030?", Now, Now);
        message.TransitionTo(InboundMessageStatus.Processing);
        message.CompleteProcessing(classification, new EmailAnalysisMetrics("fake", "synthetic-model", 1, 1, null, 1, null));
        return InboundMessage.Restore(
            id,
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
            InboundMessageStatus.Processed,
            message.ContentFingerprint,
            message.Attachments,
            classification,
            new EmailAnalysisMetrics("fake", "synthetic-model", 1, 1, null, 1, null),
            processingAttemptCount: 1);
    }

    private static EmailClassification Classification(EmailCategory category = EmailCategory.SalesInquiry) => EmailClassification.Create(
        category,
        EmailPriority.Normal,
        "The sender refers to invoice INV-2030 and an unknown reference.",
        "en",
        [
            new EmailFact(EmailFactType.InvoiceNumber, "INV-2030"),
            new EmailFact(EmailFactType.OrderReference, "unknown-ref"),
        ],
        ["order status"],
        [],
        [],
        [EmailReviewReason.UnverifiedBusinessFact],
        Now);
}
