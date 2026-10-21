using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartInbox.Application.Messages.Ports;
using SmartInbox.Application.WorkItems.Models;
using SmartInbox.Application.WorkItems.Ports;
using SmartInbox.Domain.Messages;
using SmartInbox.Domain.WorkItems;
using SmartInbox.Infrastructure.WorkItems.Persistence;

namespace SmartInbox.Infrastructure.Tests.WorkItems;

[Collection(PostgresCollection.Name)]
public sealed class WorkItemPostgresTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Successful_classification_creates_one_work_item_with_unverified_facts_and_original_result()
    {
        await fixture.ResetAsync();
        var messageId = await AddProcessedMessageAsync();
        using var services = fixture.CreateServiceProvider();
        using var scope = services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IWorkItemRepository>();
        var itemId = await GetWorkItemIdAsync(messageId);
        var item = Assert.IsType<WorkItem>(await repository.GetByIdAsync(itemId));
        var original = await scope.ServiceProvider.GetRequiredService<IInboundMessageRepository>().GetByIdAsync(messageId);

        Assert.Equal(WorkItemStatus.PendingReview, item.Status);
        Assert.Equal(0, item.Version);
        Assert.Equal(ReviewedFactStatus.Unverified, Assert.Single(item.Facts).Status);
        var creationHistory = await repository.GetHistoryAsync(item.Id);
        Assert.NotNull(creationHistory);
        Assert.Contains(creationHistory, entry => entry.Action == WorkItemAuditAction.Created);
        Assert.Equal(EmailCategory.SalesInquiry, original!.Classification!.Category);
        Assert.True(original.Classification.RequiresReview);

        await using var context = fixture.CreateContext();
        context.WorkItems.Add(new WorkItemRecord
        {
            Id = Guid.NewGuid(),
            SourceMessageId = messageId,
            Status = WorkItemStatus.PendingReview.ToString(),
            EffectiveCategory = EmailCategory.SalesInquiry.ToString(),
            EffectivePriority = EmailPriority.Normal.ToString(),
            MissingInformationJson = "[]",
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Contains("ux_work_items_source_message_id", duplicate.InnerException?.Message ?? duplicate.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Concurrent_work_item_initialization_is_rejected_by_the_unique_source_constraint()
    {
        await fixture.ResetAsync();
        var messageId = await AddProcessedMessageAsync();
        await using (var setup = fixture.CreateContext())
            await setup.WorkItems.Where(item => item.SourceMessageId == messageId).ExecuteDeleteAsync();

        async Task<bool> InsertCandidateAsync()
        {
            await using var context = fixture.CreateContext();
            context.WorkItems.Add(new WorkItemRecord
            {
                Id = Guid.NewGuid(),
                SourceMessageId = messageId,
                Status = WorkItemStatus.PendingReview.ToString(),
                EffectiveCategory = EmailCategory.SalesInquiry.ToString(),
                EffectivePriority = EmailPriority.Normal.ToString(),
                MissingInformationJson = "[]",
                CreatedAt = Now,
                UpdatedAt = Now,
            });
            try
            {
                await context.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException)
            {
                return false;
            }
        }

        var results = await Task.WhenAll(InsertCandidateAsync(), InsertCandidateAsync());

        Assert.Single(results, inserted => inserted);
        Assert.Single(results, inserted => !inserted);
        await using var verification = fixture.CreateContext();
        Assert.Equal(1, await verification.WorkItems.CountAsync(item => item.SourceMessageId == messageId));
    }

    [Fact]
    public async Task Migration_backfills_preexisting_processed_messages_into_the_review_queue()
    {
        await fixture.ResetAsync();
        var messageId = await AddProcessedMessageAsync();

        await using (var context = fixture.CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                "DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20261021122611_AddHumanReviewAndDrafts'");
            await context.Database.ExecuteSqlRawAsync(
                "DROP TABLE response_draft_revisions, work_item_audit_entries, work_item_facts, response_drafts, work_items CASCADE");
            await context.Database.MigrateAsync();
        }

        var itemId = await GetWorkItemIdAsync(messageId);
        using var services = fixture.CreateServiceProvider();
        using var scope = services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IWorkItemRepository>();
        var item = await repository.GetByIdAsync(itemId);
        var audit = await repository.GetHistoryAsync(itemId);

        Assert.Equal(WorkItemStatus.PendingReview, item!.Status);
        Assert.Equal(ReviewedFactStatus.Unverified, Assert.Single(item.Facts).Status);
        Assert.Contains(audit!, entry => entry.Action == WorkItemAuditAction.Created &&
                                         entry.Summary.Contains("backfilled", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Review_save_and_audit_are_durable_and_stale_versions_are_rejected()
    {
        await fixture.ResetAsync();
        var messageId = await AddProcessedMessageAsync();
        var itemId = await GetWorkItemIdAsync(messageId);
        using var firstServices = fixture.CreateServiceProvider();
        using var secondServices = fixture.CreateServiceProvider();
        using var firstScope = firstServices.CreateScope();
        using var secondScope = secondServices.CreateScope();
        var firstRepository = firstScope.ServiceProvider.GetRequiredService<IWorkItemRepository>();
        var secondRepository = secondScope.ServiceProvider.GetRequiredService<IWorkItemRepository>();
        var first = await firstRepository.GetByIdAsync(itemId);
        var stale = await secondRepository.GetByIdAsync(itemId);
        Assert.NotNull(first);
        Assert.NotNull(stale);
        await using (var context = fixture.CreateContext())
            Assert.Equal(0, await context.WorkItems.Where(record => record.Id == itemId).Select(record => record.Version).SingleAsync());

        first.StartReview("reviewer-1", Now.AddMinutes(1));
        await firstRepository.SaveAsync(first);
        stale.StartReview("reviewer-2", Now.AddMinutes(1));
        await Assert.ThrowsAsync<WorkItemConcurrencyException>(() => secondRepository.SaveAsync(stale));

        var current = await firstRepository.GetByIdAsync(itemId);
        Assert.NotNull(current);
        current.UpdateReview(EmailCategory.SupportRequest, EmailPriority.High, "Synthetic correction", [], "reviewer-1", Now.AddMinutes(2));
        await firstRepository.SaveAsync(current);
        current = await firstRepository.GetByIdAsync(itemId);
        Assert.NotNull(current);
        current.VerifyFact(current.Facts[0].Id, "reviewer-1", Now.AddMinutes(3));
        await firstRepository.SaveAsync(current);

        var persisted = await secondRepository.GetByIdAsync(itemId);
        Assert.Equal(EmailCategory.SupportRequest, persisted!.EffectiveCategory);
        Assert.Equal(ReviewedFactStatus.Verified, Assert.Single(persisted.Facts).Status);
        var history = await secondRepository.GetHistoryAsync(itemId);
        Assert.NotNull(history);
        Assert.Contains(history, entry => entry.Action == WorkItemAuditAction.CategoryCorrected);
        Assert.Contains(history, entry => entry.Action == WorkItemAuditAction.FactVerified);
        var original = await secondScope.ServiceProvider.GetRequiredService<IInboundMessageRepository>().GetByIdAsync(messageId);
        Assert.Equal(EmailCategory.SalesInquiry, original!.Classification!.Category);
        Assert.Equal(EmailPriority.Normal, original.Classification.Priority);
    }

    [Fact]
    public async Task Draft_edits_keep_approved_revisions_after_a_fresh_context_load()
    {
        await fixture.ResetAsync();
        var messageId = await AddProcessedMessageAsync(senderEmail: "alex@example.test");
        var itemId = await GetWorkItemIdAsync(messageId);
        using var services = fixture.CreateServiceProvider();
        using var scope = services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IWorkItemRepository>();
        var item = await repository.GetByIdAsync(itemId);
        Assert.NotNull(item);
        item.StartReview("reviewer-1", Now.AddMinutes(1));
        await repository.SaveAsync(item);
        item = await repository.GetByIdAsync(itemId);
        Assert.NotNull(item);
        item.SaveDraft("alex@example.test", "Synthetic response", "We received your message.", "reviewer-1", Now.AddMinutes(2));
        await repository.SaveAsync(item);
        item = await repository.GetByIdAsync(itemId);
        Assert.NotNull(item);
        item.ApproveDraft(1, "reviewer-1", Now.AddMinutes(3));
        await repository.SaveAsync(item);
        item = await repository.GetByIdAsync(itemId);
        Assert.NotNull(item);
        item.SaveDraft("alex@example.test", "Synthetic response", "Could you confirm the invoice number?", "reviewer-1", Now.AddMinutes(4));
        await repository.SaveAsync(item);

        var persisted = await repository.GetByIdAsync(itemId);
        Assert.NotNull(persisted?.Draft);
        Assert.Equal(ResponseDraftStatus.Approved, persisted.Draft.Revisions[0].Status);
        Assert.Equal(ResponseDraftStatus.Draft, persisted.Draft.CurrentRevision.Status);
        Assert.Equal(2, persisted.Draft.CurrentRevision.Revision);
        var history = await repository.GetHistoryAsync(itemId);
        Assert.NotNull(history);
        Assert.Contains(history, entry => entry.Action == WorkItemAuditAction.DraftApproved);
        Assert.Contains(history, entry => entry.Action == WorkItemAuditAction.DraftEdited);
    }

    private async Task<Guid> AddProcessedMessageAsync(string senderEmail = "sender@example.test")
    {
        using var services = fixture.CreateServiceProvider();
        using var scope = services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IInboundMessageRepository>();
        var message = InboundMessage.Receive(
            "synthetic", "test-mailbox", Guid.NewGuid().ToString("N"), null, senderEmail, "Synthetic sender",
            "Synthetic invoice", "Invoice INV-2030 needs review.", Now, Now);
        await repository.AddIfAbsentAsync(message);
        using var processingServices = fixture.CreateServiceProvider();
        using var processingScope = processingServices.CreateScope();
        var processingRepository = processingScope.ServiceProvider.GetRequiredService<IInboundMessageRepository>();
        var owner = Guid.NewGuid();
        Assert.NotNull(await processingRepository.ClaimNextForProcessingAsync(owner, Now, TimeSpan.FromMinutes(1), 3));
        Assert.True(await processingRepository.CompleteProcessingAsync(
            message.Id,
            owner,
            EmailClassification.Create(
                EmailCategory.SalesInquiry,
                EmailPriority.Normal,
                "The sender refers to invoice INV-2030.",
                "en",
                [new EmailFact(EmailFactType.InvoiceNumber, "INV-2030")],
                [],
                [],
                [],
                [EmailReviewReason.UnverifiedBusinessFact],
                Now.AddSeconds(1)),
            new EmailAnalysisMetrics("fake", "synthetic-model", 1, 1, null, 1, null)));
        return message.Id;
    }

    private async Task<Guid> GetWorkItemIdAsync(Guid messageId)
    {
        using var services = fixture.CreateServiceProvider();
        using var scope = services.CreateScope();
        var workItems = scope.ServiceProvider.GetRequiredService<IWorkItemRepository>();
        var page = await workItems.ListAsync(new WorkItemListFilter(1, 100, null, null, null, null, null, null));
        return Assert.Single(page.Items, entry => entry.SourceMessageId == messageId).Id;
    }
}
