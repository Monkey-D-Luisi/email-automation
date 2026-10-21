using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartInbox.Infrastructure.WorkItems.Persistence;

internal sealed class GmailDraftExportRecordConfiguration : IEntityTypeConfiguration<GmailDraftExportRecord>
{
    public void Configure(EntityTypeBuilder<GmailDraftExportRecord> builder)
    {
        builder.ToTable("gmail_draft_exports", table =>
        {
            table.HasCheckConstraint("ck_gmail_draft_exports_status", "\"Status\" IN ('Pending', 'InProgress', 'Succeeded', 'Failed', 'NeedsReconciliation', 'Cancelled')");
            table.HasCheckConstraint("ck_gmail_draft_exports_revision", "\"ApprovedRevision\" > 0");
            table.HasCheckConstraint("ck_gmail_draft_exports_attempt_count", "\"AttemptCount\" >= 0");
            table.HasCheckConstraint("ck_gmail_draft_exports_fingerprint", "\"ContentFingerprint\" ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("ck_gmail_draft_exports_content", "char_length(\"Subject\") BETWEEN 1 AND 998 AND char_length(\"Body\") BETWEEN 1 AND 20000");
            table.HasCheckConstraint("ck_gmail_draft_exports_state", "(\"Status\" = 'Pending' AND \"ClaimedAt\" IS NULL AND \"LeaseExpiresAt\" IS NULL AND \"CompletedAt\" IS NULL AND \"GmailDraftId\" IS NULL AND \"FailureKind\" IS NULL) OR (\"Status\" = 'InProgress' AND \"AttemptId\" IS NOT NULL AND \"ClaimedAt\" IS NOT NULL AND \"LeaseExpiresAt\" IS NOT NULL AND \"CompletedAt\" IS NULL AND \"GmailDraftId\" IS NULL AND \"FailureKind\" IS NULL) OR (\"Status\" = 'Succeeded' AND \"AttemptId\" IS NOT NULL AND \"ClaimedAt\" IS NULL AND \"LeaseExpiresAt\" IS NULL AND \"CompletedAt\" IS NOT NULL AND \"GmailDraftId\" IS NOT NULL AND \"FailureKind\" IS NULL) OR (\"Status\" IN ('Failed', 'NeedsReconciliation') AND \"AttemptId\" IS NOT NULL AND \"ClaimedAt\" IS NULL AND \"LeaseExpiresAt\" IS NULL AND \"CompletedAt\" IS NOT NULL AND \"GmailDraftId\" IS NULL AND \"FailureKind\" IS NOT NULL) OR (\"Status\" = 'Cancelled' AND \"ClaimedAt\" IS NULL AND \"LeaseExpiresAt\" IS NULL AND \"CompletedAt\" IS NOT NULL AND \"GmailDraftId\" IS NULL AND \"FailureKind\" IS NULL)");
        });
        builder.HasKey(export => export.Id);
        builder.HasIndex(export => new
            {
                export.DraftId,
                export.ApprovedRevision,
                export.DestinationMailboxId,
                export.RecipientEmail,
            })
            .IsUnique()
            .HasDatabaseName("ux_gmail_draft_exports_draft_revision_destination_recipient");
        builder.HasIndex(export => new { export.Status, export.CreatedAt })
            .HasDatabaseName("ix_gmail_draft_exports_status_created_at");
        builder.HasIndex(export => new { export.WorkItemId, export.CreatedAt })
            .HasDatabaseName("ix_gmail_draft_exports_work_item_created_at");
        builder.Property(export => export.DestinationMailboxId).HasMaxLength(256).IsRequired();
        builder.Property(export => export.RecipientEmail).HasMaxLength(320).IsRequired();
        builder.Property(export => export.Subject).HasMaxLength(998).IsRequired();
        builder.Property(export => export.Body).HasMaxLength(20_000).IsRequired();
        builder.Property(export => export.ContentFingerprint).HasMaxLength(64).IsRequired();
        builder.Property(export => export.RequestedBy).HasMaxLength(128).IsRequired();
        builder.Property(export => export.Status).HasMaxLength(32).IsRequired();
        builder.Property(export => export.GmailDraftId).HasMaxLength(512);
        builder.Property(export => export.FailureKind).HasMaxLength(64);
        builder.HasOne<WorkItemRecord>().WithMany().HasForeignKey(export => export.WorkItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
