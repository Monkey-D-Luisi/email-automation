using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartInbox.Application.Messages.Ports;
using SmartInbox.Application.WorkItems.Models;
using SmartInbox.Application.WorkItems.Ports;
using SmartInbox.Domain.Messages;
using SmartInbox.Domain.WorkItems;
using SmartInbox.Infrastructure.Persistence;
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
                "DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20261022103328_AddOutlookAuditActions'");
            await context.Database.ExecuteSqlRawAsync(
                "DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20261022101620_AddOutlookProviderSupport'");
            await context.Database.ExecuteSqlRawAsync(
                "DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20261021142118_AddGmailDraftExports'");
            await context.Database.ExecuteSqlRawAsync(
                "DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20261021122611_AddHumanReviewAndDrafts'");
            await context.Database.ExecuteSqlRawAsync(
                "DROP TABLE gmail_draft_exports, response_draft_revisions, work_item_audit_entries, work_item_facts, response_drafts, work_items CASCADE");
            await context.Database.ExecuteSqlRawAsync(
                "ALTER TABLE inbound_messages DROP COLUMN \"ExternalInternetMessageId\", DROP COLUMN \"ExternalRecipientEmailsJson\"");
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

    [Fact]
    public async Task Gmail_export_is_idempotent_and_only_one_postgres_worker_can_claim_it()
    {
        await fixture.ResetAsync();
        var (workItemId, requested) = await CreatePendingExportAsync();
        using var services = fixture.CreateServiceProvider();
        using var secondServices = fixture.CreateServiceProvider();
        using var firstScope = services.CreateScope();
        using var secondScope = secondServices.CreateScope();
        var firstRepository = firstScope.ServiceProvider.GetRequiredService<IGmailDraftExportRepository>();
        var secondRepository = secondScope.ServiceProvider.GetRequiredService<IGmailDraftExportRepository>();

        var duplicate = await firstRepository.CreateOrGetAsync(requested, expectedWorkItemVersion: 3);
        Assert.False(duplicate.Created);
        Assert.Equal(requested.Id, duplicate.Export.Id);

        var claims = await Task.WhenAll(
            firstRepository.ClaimPendingAsync("test-mailbox", TimeSpan.FromMinutes(5), Now.AddMinutes(5)),
            secondRepository.ClaimPendingAsync("test-mailbox", TimeSpan.FromMinutes(5), Now.AddMinutes(5)));
        var claim = Assert.Single(claims, value => value is not null)!;
        var claimantRepository = claims[0] is not null ? firstRepository : secondRepository;
        var duplicateCallbackRepository = claims[0] is not null ? secondRepository : firstRepository;
        Assert.Equal(GmailDraftExportStatus.InProgress, claim.Status);
        Assert.Equal(requested.Subject, claim.Subject);
        Assert.Equal(requested.Body, claim.Body);
        Assert.Equal(requested.ContentFingerprint, claim.ContentFingerprint);

        Assert.Equal(GmailDraftExportTransition.Applied, await claimantRepository.CompleteAsync(
            claim.Id, claim.AttemptId!.Value, "synthetic-gmail-draft-1", Now.AddMinutes(6)));
        Assert.Equal(GmailDraftExportTransition.Duplicate, await duplicateCallbackRepository.CompleteAsync(
            claim.Id, claim.AttemptId.Value, "synthetic-gmail-draft-1", Now.AddMinutes(6)));
        var persisted = Assert.Single(await firstRepository.ListByWorkItemAsync(workItemId) ?? []);
        Assert.Equal(GmailDraftExportStatus.Succeeded, persisted.Status);
        Assert.Equal("synthetic-gmail-draft-1", persisted.GmailDraftId);
        Assert.Contains(await firstScope.ServiceProvider.GetRequiredService<IWorkItemRepository>().GetHistoryAsync(workItemId) ?? [],
            entry => entry.Action == WorkItemAuditAction.GmailDraftExportSucceeded);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Duplicate_gmail_export_callback_refreshes_a_stale_tracked_export(bool firstRepositoryWins)
    {
        await fixture.ResetAsync();
        var (_, requested) = await CreatePendingExportAsync();
        using var firstServices = fixture.CreateServiceProvider();
        using var secondServices = fixture.CreateServiceProvider();
        using var firstScope = firstServices.CreateScope();
        using var secondScope = secondServices.CreateScope();
        var firstRepository = firstScope.ServiceProvider.GetRequiredService<IGmailDraftExportRepository>();
        var secondRepository = secondScope.ServiceProvider.GetRequiredService<IGmailDraftExportRepository>();
        var firstContext = firstScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var secondContext = secondScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var winnerRepository = firstRepositoryWins ? firstRepository : secondRepository;
        var losingRepository = firstRepositoryWins ? secondRepository : firstRepository;
        var losingContext = firstRepositoryWins ? secondContext : firstContext;

        var existing = await losingRepository.CreateOrGetAsync(requested, expectedWorkItemVersion: 3);
        Assert.False(existing.Created);
        Assert.Empty(losingContext.ChangeTracker.Entries<GmailDraftExportRecord>());
        var trackedPending = await losingContext.GmailDraftExports.SingleAsync(item => item.Id == requested.Id);
        Assert.Equal(GmailDraftExportStatus.Pending.ToString(), trackedPending.Status);

        var claim = await winnerRepository.ClaimPendingAsync("test-mailbox", TimeSpan.FromMinutes(5), Now.AddMinutes(5));
        Assert.NotNull(claim);
        var attemptId = Assert.IsType<Guid>(claim.AttemptId);
        Assert.Equal(GmailDraftExportTransition.Applied, await winnerRepository.CompleteAsync(
            claim.Id, attemptId, "synthetic-gmail-draft-1", Now.AddMinutes(6)));
        Assert.Equal(GmailDraftExportTransition.Duplicate, await losingRepository.CompleteAsync(
            claim.Id, attemptId, "synthetic-gmail-draft-1", Now.AddMinutes(6)));
    }

    [Fact]
    public async Task Retry_requires_the_current_approved_revision_when_an_older_graph_is_tracked()
    {
        await fixture.ResetAsync();
        var (workItemId, requested) = await CreatePendingExportAsync();
        using var exportServices = fixture.CreateServiceProvider();
        using var exportScope = exportServices.CreateScope();
        var exports = exportScope.ServiceProvider.GetRequiredService<IGmailDraftExportRepository>();
        var claim = Assert.IsType<GmailDraftExport>(await exports.ClaimPendingAsync(
            "test-mailbox", TimeSpan.FromMinutes(5), Now.AddMinutes(5)));
        var context = exportScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var trackedWorkItem = Assert.Single(context.WorkItems.Local, item => item.Id == workItemId);
        await context.Entry(trackedWorkItem).Reference(item => item.Draft).LoadAsync();
        var trackedDraft = Assert.IsType<ResponseDraftRecord>(trackedWorkItem.Draft);
        await context.Entry(trackedDraft).Collection(draft => draft.Revisions).LoadAsync();
        Assert.Equal(1, Assert.Single(trackedDraft.Revisions).Revision);
        Assert.Equal(GmailDraftExportTransition.Applied, await exports.FailAsync(
            claim.Id, claim.AttemptId!.Value, GmailDraftExportFailureKind.ProviderOutcomeUnknown, Now.AddMinutes(6)));

        using var reviewerServices = fixture.CreateServiceProvider();
        using var reviewerScope = reviewerServices.CreateScope();
        var workItems = reviewerScope.ServiceProvider.GetRequiredService<IWorkItemRepository>();
        var workItem = Assert.IsType<WorkItem>(await workItems.GetByIdAsync(workItemId));
        workItem.SaveDraft("alex@example.test", "Synthetic response", "A new unapproved revision.", "reviewer-1", Now.AddMinutes(7));
        await workItems.SaveAsync(workItem);
        var currentWorkItem = Assert.IsType<WorkItem>(await workItems.GetByIdAsync(workItemId));

        await Assert.ThrowsAsync<WorkItemStateConflictException>(() => exports.RetryAfterManualCheckAsync(
            workItemId,
            requested.Id,
            currentWorkItem.Version,
            "test-mailbox",
            "reviewer-1",
            Now.AddMinutes(8)));
        var persisted = Assert.Single(await exports.ListByWorkItemAsync(workItemId) ?? []);
        Assert.Equal(GmailDraftExportStatus.NeedsReconciliation, persisted.Status);
    }

    [Fact]
    public async Task Expired_gmail_export_claim_requires_reconciliation_and_never_returns_to_the_queue()
    {
        await fixture.ResetAsync();
        var (workItemId, requested) = await CreatePendingExportAsync();
        using var services = fixture.CreateServiceProvider();
        using var scope = services.CreateScope();
        var exports = scope.ServiceProvider.GetRequiredService<IGmailDraftExportRepository>();
        var claim = Assert.IsType<GmailDraftExport>(await exports.ClaimPendingAsync(
            "test-mailbox", TimeSpan.FromMinutes(5), Now.AddMinutes(5)));

        Assert.Null(await exports.ClaimPendingAsync("test-mailbox", TimeSpan.FromMinutes(5), Now.AddMinutes(11)));
        var unresolved = Assert.Single(await exports.ListByWorkItemAsync(workItemId) ?? []);
        Assert.Equal(GmailDraftExportStatus.NeedsReconciliation, unresolved.Status);
        Assert.Equal(GmailDraftExportFailureKind.ProviderOutcomeUnknown, unresolved.FailureKind);

        var workItem = await scope.ServiceProvider.GetRequiredService<IWorkItemRepository>().GetByIdAsync(workItemId);
        Assert.NotNull(workItem);
        var reconciled = await exports.ReconcileAsync(
            workItemId, requested.Id, workItem.Version, "test-mailbox", "reviewer-1",
            "synthetic-gmail-draft-reconciled", Now.AddMinutes(12));
        Assert.Equal(GmailDraftExportStatus.Succeeded, reconciled!.Status);
        Assert.Equal("synthetic-gmail-draft-reconciled", reconciled.GmailDraftId);
        Assert.Equal(claim.AttemptId, reconciled.AttemptId);
    }

    [Theory]
    [InlineData(DraftExportProvider.Gmail)]
    [InlineData(DraftExportProvider.Outlook)]
    public async Task Expiring_a_claim_leaves_pending_work_for_the_next_poll_and_other_providers_untouched(DraftExportProvider provider)
    {
        await fixture.ResetAsync();
        var (expiredWorkItemId, _) = await CreatePendingExportAsync(provider);
        using var services = fixture.CreateServiceProvider();
        using var scope = services.CreateScope();
        var exports = scope.ServiceProvider.GetRequiredService<IGmailDraftExportRepository>();
        var expiredClaim = Assert.IsType<GmailDraftExport>(await exports.ClaimPendingAsync(
            provider, "test-mailbox", TimeSpan.FromMinutes(5), Now.AddMinutes(5)));
        var otherProvider = provider == DraftExportProvider.Gmail ? DraftExportProvider.Outlook : DraftExportProvider.Gmail;
        var (otherWorkItemId, _) = await CreatePendingExportAsync(otherProvider);
        var otherClaim = Assert.IsType<GmailDraftExport>(await exports.ClaimPendingAsync(
            otherProvider, "test-mailbox", TimeSpan.FromMinutes(5), Now.AddMinutes(5)));
        var (pendingWorkItemId, pending) = await CreatePendingExportAsync(provider);

        Assert.Null(await exports.ClaimPendingAsync(provider, "test-mailbox", TimeSpan.FromMinutes(5), Now.AddMinutes(10)));

        var expired = Assert.Single(await exports.ListByWorkItemAsync(expiredWorkItemId) ?? []);
        Assert.Equal(GmailDraftExportStatus.NeedsReconciliation, expired.Status);
        Assert.Equal(GmailDraftExportFailureKind.ProviderOutcomeUnknown, expired.FailureKind);
        Assert.Equal(expiredClaim.AttemptId, expired.AttemptId);
        Assert.Null(expired.LeaseExpiresAt);
        var stillPending = Assert.Single(await exports.ListByWorkItemAsync(pendingWorkItemId) ?? []);
        Assert.Equal(GmailDraftExportStatus.Pending, stillPending.Status);
        Assert.Equal(0, stillPending.AttemptCount);
        Assert.Equal(GmailDraftExportStatus.InProgress, Assert.Single(await exports.ListByWorkItemAsync(otherWorkItemId) ?? []).Status);

        var workItems = scope.ServiceProvider.GetRequiredService<IWorkItemRepository>();
        var auditAction = provider == DraftExportProvider.Gmail
            ? WorkItemAuditAction.GmailDraftExportNeedsReconciliation
            : WorkItemAuditAction.OutlookDraftExportNeedsReconciliation;
        var audit = Assert.Single(await workItems.GetHistoryAsync(expiredWorkItemId) ?? [], entry => entry.Action == auditAction);
        Assert.Equal(expired.Id, audit.EntityId);
        Assert.Equal(expired.ApprovedRevision, audit.Revision);
        Assert.Equal(provider == DraftExportProvider.Gmail ? "gmail-n8n" : "outlook-n8n", audit.ActorId);
        Assert.Equal($"The {provider} draft export claim expired; provider outcome requires manual reconciliation.", audit.Summary);

        var nextClaim = Assert.IsType<GmailDraftExport>(await exports.ClaimPendingAsync(
            provider, "test-mailbox", TimeSpan.FromMinutes(5), Now.AddMinutes(10)));
        Assert.Equal(pending.Id, nextClaim.Id);
        Assert.Equal(1, nextClaim.AttemptCount);
        var untouched = Assert.Single(await exports.ListByWorkItemAsync(otherWorkItemId) ?? []);
        Assert.Equal(GmailDraftExportStatus.InProgress, untouched.Status);
        Assert.Equal(otherClaim.AttemptId, untouched.AttemptId);
    }

    [Theory]
    [InlineData(DraftExportProvider.Gmail)]
    [InlineData(DraftExportProvider.Outlook)]
    public async Task Claim_cancels_a_pending_export_when_its_destination_mailbox_is_no_longer_configured(DraftExportProvider provider)
    {
        await fixture.ResetAsync();
        var (workItemId, requested) = await CreatePendingExportAsync(provider);
        using var services = fixture.CreateServiceProvider();
        using var scope = services.CreateScope();
        var exports = scope.ServiceProvider.GetRequiredService<IGmailDraftExportRepository>();

        Assert.Null(await exports.ClaimPendingAsync(provider, "replacement-mailbox", TimeSpan.FromMinutes(5), Now.AddMinutes(5)));

        var cancelled = Assert.Single(await exports.ListByWorkItemAsync(workItemId) ?? []);
        Assert.Equal(requested.Id, cancelled.Id);
        Assert.Equal(GmailDraftExportStatus.Cancelled, cancelled.Status);
        Assert.Equal(0, cancelled.AttemptCount);
        Assert.Null(cancelled.AttemptId);
        Assert.Equal(requested.ContentFingerprint, cancelled.ContentFingerprint);
        Assert.Equal(Now.AddMinutes(5), cancelled.CompletedAt);
        Assert.Null(await exports.ClaimPendingAsync(provider, "test-mailbox", TimeSpan.FromMinutes(5), Now.AddMinutes(6)));
        var auditAction = provider == DraftExportProvider.Gmail
            ? WorkItemAuditAction.GmailDraftExportCancelled
            : WorkItemAuditAction.OutlookDraftExportCancelled;
        var audit = Assert.Single(await scope.ServiceProvider.GetRequiredService<IWorkItemRepository>().GetHistoryAsync(workItemId) ?? [],
            entry => entry.Action == auditAction);
        Assert.Equal(requested.Id, audit.EntityId);
        Assert.Equal(provider == DraftExportProvider.Gmail ? "gmail-n8n" : "outlook-n8n", audit.ActorId);
        Assert.Equal($"Pending {provider} draft export cancelled because its destination is no longer configured.", audit.Summary);
    }

    [Theory]
    [InlineData(DraftExportProvider.Gmail)]
    [InlineData(DraftExportProvider.Outlook)]
    public async Task Claim_cancels_a_pending_export_after_the_work_item_is_resolved(DraftExportProvider provider)
    {
        await fixture.ResetAsync();
        var (workItemId, requested) = await CreatePendingExportAsync(provider);
        using var services = fixture.CreateServiceProvider();
        using var scope = services.CreateScope();
        var exports = scope.ServiceProvider.GetRequiredService<IGmailDraftExportRepository>();
        var workItems = scope.ServiceProvider.GetRequiredService<IWorkItemRepository>();
        var item = Assert.IsType<WorkItem>(await workItems.GetByIdAsync(workItemId));
        item.RejectFact(Assert.Single(item.Facts).Id, "reviewer-1", Now.AddMinutes(5));
        await workItems.SaveAsync(item);
        item = Assert.IsType<WorkItem>(await workItems.GetByIdAsync(workItemId));
        item.Resolve("reviewer-1", Now.AddMinutes(6));
        await workItems.SaveAsync(item);
        Assert.Equal(GmailDraftExportStatus.Pending, Assert.Single(await exports.ListByWorkItemAsync(workItemId) ?? []).Status);

        Assert.Null(await exports.ClaimPendingAsync(provider, "test-mailbox", TimeSpan.FromMinutes(5), Now.AddMinutes(7)));

        var cancelled = Assert.Single(await exports.ListByWorkItemAsync(workItemId) ?? []);
        Assert.Equal(requested.Id, cancelled.Id);
        Assert.Equal(GmailDraftExportStatus.Cancelled, cancelled.Status);
        Assert.Equal(0, cancelled.AttemptCount);
        Assert.Null(cancelled.AttemptId);
        Assert.Equal(requested.ContentFingerprint, cancelled.ContentFingerprint);
        Assert.Equal(Now.AddMinutes(7), cancelled.CompletedAt);
        var auditAction = provider == DraftExportProvider.Gmail
            ? WorkItemAuditAction.GmailDraftExportCancelled
            : WorkItemAuditAction.OutlookDraftExportCancelled;
        var audit = Assert.Single(await workItems.GetHistoryAsync(workItemId) ?? [], entry => entry.Action == auditAction);
        Assert.Equal(requested.Id, audit.EntityId);
        Assert.Equal(provider == DraftExportProvider.Gmail ? "gmail-n8n" : "outlook-n8n", audit.ActorId);
        Assert.Equal($"Pending {provider} draft export cancelled because its approved revision is no longer current.", audit.Summary);
    }

    [Fact]
    public async Task Outlook_unknown_export_audit_names_the_provider_for_manual_reconciliation()
    {
        await fixture.ResetAsync();
        var (workItemId, _) = await CreatePendingExportAsync(DraftExportProvider.Outlook);
        using var services = fixture.CreateServiceProvider();
        using var scope = services.CreateScope();
        var exports = scope.ServiceProvider.GetRequiredService<IGmailDraftExportRepository>();
        var claim = Assert.IsType<GmailDraftExport>(await exports.ClaimPendingAsync(
            DraftExportProvider.Outlook, "test-mailbox", TimeSpan.FromMinutes(5), Now.AddMinutes(5)));

        Assert.Equal(GmailDraftExportTransition.Applied, await exports.FailAsync(
            DraftExportProvider.Outlook,
            claim.Id,
            claim.AttemptId!.Value,
            GmailDraftExportFailureKind.ProviderOutcomeUnknown,
            Now.AddMinutes(6)));

        var audit = Assert.Single(
            await scope.ServiceProvider.GetRequiredService<IWorkItemRepository>().GetHistoryAsync(workItemId) ?? [],
            entry => entry.Action == WorkItemAuditAction.OutlookDraftExportNeedsReconciliation);
        Assert.Contains("Outlook draft creation", audit.Summary);
        Assert.DoesNotContain("Gmail draft creation", audit.Summary);
    }

    [Fact]
    public async Task Editing_the_approved_draft_cancels_a_pending_export_before_dispatch()
    {
        await fixture.ResetAsync();
        var (workItemId, requested) = await CreatePendingExportAsync();
        using var services = fixture.CreateServiceProvider();
        using var scope = services.CreateScope();
        var workItems = scope.ServiceProvider.GetRequiredService<IWorkItemRepository>();
        var exports = scope.ServiceProvider.GetRequiredService<IGmailDraftExportRepository>();
        var item = await workItems.GetByIdAsync(workItemId);
        Assert.NotNull(item);
        item.SaveDraft("alex@example.test", "Synthetic response", "A new unapproved revision.", "reviewer-1", Now.AddMinutes(10));
        await workItems.SaveAsync(item);

        Assert.Null(await exports.ClaimPendingAsync("test-mailbox", TimeSpan.FromMinutes(5), Now.AddMinutes(11)));
        var cancelled = Assert.Single(await exports.ListByWorkItemAsync(workItemId) ?? []);
        Assert.Equal(requested.Id, cancelled.Id);
        Assert.Equal(GmailDraftExportStatus.Cancelled, cancelled.Status);
        Assert.Contains(await workItems.GetHistoryAsync(workItemId) ?? [],
            entry => entry.Action == WorkItemAuditAction.GmailDraftExportCancelled);
    }

    private async Task<(Guid WorkItemId, GmailDraftExport Export)> CreatePendingExportAsync(
        DraftExportProvider provider = DraftExportProvider.Gmail)
    {
        var messageId = await AddProcessedMessageAsync(senderEmail: "alex@example.test");
        var workItemId = await GetWorkItemIdAsync(messageId);
        using var services = fixture.CreateServiceProvider();
        using var scope = services.CreateScope();
        var workItems = scope.ServiceProvider.GetRequiredService<IWorkItemRepository>();
        var item = await workItems.GetByIdAsync(workItemId);
        Assert.NotNull(item);
        item.StartReview("reviewer-1", Now.AddMinutes(1));
        await workItems.SaveAsync(item);
        item = await workItems.GetByIdAsync(workItemId);
        Assert.NotNull(item);
        item.SaveDraft("alex@example.test", "Synthetic response", "We received your message.", "reviewer-1", Now.AddMinutes(2));
        await workItems.SaveAsync(item);
        item = await workItems.GetByIdAsync(workItemId);
        Assert.NotNull(item);
        item.ApproveDraft(1, "reviewer-1", Now.AddMinutes(3));
        await workItems.SaveAsync(item);

        var export = GmailDraftExport.Create(item, "test-mailbox", "alex@example.test", "reviewer-1", Now.AddMinutes(4), provider);
        var result = await scope.ServiceProvider.GetRequiredService<IGmailDraftExportRepository>()
            .CreateOrGetAsync(export, item.Version);
        Assert.True(result.Created);
        return (workItemId, export);
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
