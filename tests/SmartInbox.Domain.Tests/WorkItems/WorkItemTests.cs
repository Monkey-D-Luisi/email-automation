using SmartInbox.Domain.Messages;
using SmartInbox.Domain.WorkItems;

namespace SmartInbox.Domain.Tests.WorkItems;

public sealed class WorkItemTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Review_changes_are_separate_from_original_classification_and_audited()
    {
        var original = Classification();
        var item = WorkItem.Create(Guid.NewGuid(), original, Now);
        item.StartReview("reviewer-1", Now.AddMinutes(1));
        var fact = Assert.Single(item.Facts);

        Assert.True(item.UpdateReview(
            EmailCategory.SupportRequest,
            EmailPriority.High,
            "Please confirm the serial number.",
            ["serial number"],
            "reviewer-1",
            Now.AddMinutes(2)));
        item.VerifyFact(fact.Id, "reviewer-1", Now.AddMinutes(3));

        Assert.Equal(EmailCategory.SalesInquiry, original.Category);
        Assert.Equal(EmailPriority.Normal, original.Priority);
        Assert.True(original.RequiresReview);
        Assert.Equal(EmailCategory.SupportRequest, item.EffectiveCategory);
        Assert.Equal(EmailPriority.High, item.EffectivePriority);
        Assert.Equal(ReviewedFactStatus.Verified, fact.Status);
        Assert.Equal("serial number", Assert.Single(item.MissingInformation));
        Assert.Contains(item.PendingAuditEntries, entry => entry.Action == WorkItemAuditAction.CategoryCorrected);
        Assert.Contains(item.PendingAuditEntries, entry => entry.Action == WorkItemAuditAction.PriorityCorrected);
        Assert.Contains(item.PendingAuditEntries, entry => entry.Action == WorkItemAuditAction.FactVerified);
    }

    [Fact]
    public void Correcting_a_verified_fact_requires_verification_again_and_resolution_requires_reviewed_facts()
    {
        var item = WorkItem.Create(Guid.NewGuid(), Classification(), Now);
        item.StartReview("reviewer-1", Now);
        var fact = Assert.Single(item.Facts);
        item.VerifyFact(fact.Id, "reviewer-1", Now.AddMinutes(1));
        item.CorrectFact(fact.Id, "INV-2031", "reviewer-1", Now.AddMinutes(2));

        Assert.Equal(ReviewedFactStatus.Unverified, fact.Status);
        Assert.Equal("INV-2031", fact.Value);
        Assert.Throws<InvalidOperationException>(() => item.Resolve("reviewer-1", Now.AddMinutes(3)));

        item.RejectFact(fact.Id, "reviewer-1", Now.AddMinutes(4));
        item.Resolve("reviewer-1", Now.AddMinutes(5));

        Assert.Equal(WorkItemStatus.Resolved, item.Status);
        Assert.Equal("reviewer-1", item.ResolvedBy);
    }

    [Fact]
    public void Editing_an_approved_draft_creates_a_new_unapproved_revision()
    {
        var item = WorkItem.Create(Guid.NewGuid(), Classification(), Now);
        item.StartReview("reviewer-1", Now);
        item.SaveDraft("alex@example.test", "Re: Invoice", "We received your message.", "reviewer-1", Now.AddMinutes(1));
        item.ApproveDraft(1, "reviewer-1", Now.AddMinutes(2));

        item.SaveDraft("alex@example.test", "Re: Invoice", "Could you confirm the invoice number?", "reviewer-1", Now.AddMinutes(3));

        Assert.NotNull(item.Draft);
        Assert.Equal(2, item.Draft.CurrentRevision.Revision);
        Assert.Equal(ResponseDraftStatus.Approved, item.Draft.Revisions[0].Status);
        Assert.Equal(ResponseDraftStatus.Draft, item.Draft.CurrentRevision.Status);
        Assert.Throws<InvalidOperationException>(() => item.ApproveDraft(1, "reviewer-1", Now.AddMinutes(4)));
        item.RejectDraft(2, "Please review the wording.", "reviewer-1", Now.AddMinutes(4));
        Assert.Equal("Please review the wording.", item.Draft.CurrentRevision.Feedback);
    }

    [Fact]
    public void Review_requires_a_claim_and_a_valid_recipient_for_a_draft()
    {
        var item = WorkItem.Create(Guid.NewGuid(), Classification(), Now);
        Assert.Throws<InvalidOperationException>(() => item.UpdateReview(
            EmailCategory.SupportRequest, EmailPriority.Normal, null, [], "reviewer-1", Now));
        item.StartReview("reviewer-1", Now);

        Assert.Throws<ArgumentException>(() => item.SaveDraft("not-an-email", "Re: Invoice", "Response", "reviewer-1", Now));
        Assert.Throws<InvalidOperationException>(() => item.StartReview("reviewer-2", Now));
    }

    private static EmailClassification Classification() => EmailClassification.Create(
        EmailCategory.SalesInquiry,
        EmailPriority.Normal,
        "The sender refers to invoice INV-2030.",
        "en",
        [new EmailFact(EmailFactType.InvoiceNumber, "INV-2030")],
        ["company name"],
        [],
        [],
        [EmailReviewReason.UnverifiedBusinessFact],
        Now);
}
