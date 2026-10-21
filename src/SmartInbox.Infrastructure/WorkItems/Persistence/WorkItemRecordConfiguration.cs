using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartInbox.Infrastructure.WorkItems.Persistence;

internal sealed class WorkItemRecordConfiguration : IEntityTypeConfiguration<WorkItemRecord>
{
    public void Configure(EntityTypeBuilder<WorkItemRecord> builder)
    {
        builder.ToTable("work_items", table =>
        {
            table.HasCheckConstraint("ck_work_items_status", "\"Status\" IN ('PendingReview', 'InReview', 'Resolved')");
            table.HasCheckConstraint("ck_work_items_category", "\"EffectiveCategory\" IN ('SalesInquiry', 'SupportRequest', 'BillingDocument', 'GeneralInquiry', 'Irrelevant', 'Unknown')");
            table.HasCheckConstraint("ck_work_items_priority", "\"EffectivePriority\" IN ('Low', 'Normal', 'High', 'Urgent')");
            table.HasCheckConstraint("ck_work_items_version", "\"Version\" >= 0");
            table.HasCheckConstraint("ck_work_items_assignment", "(\"Status\" = 'PendingReview' AND \"AssignedReviewerId\" IS NULL) OR (\"Status\" <> 'PendingReview' AND \"AssignedReviewerId\" IS NOT NULL)");
            table.HasCheckConstraint("ck_work_items_resolution", "(\"Status\" = 'Resolved' AND \"ResolvedAt\" IS NOT NULL AND \"ResolvedBy\" IS NOT NULL) OR (\"Status\" <> 'Resolved' AND \"ResolvedAt\" IS NULL AND \"ResolvedBy\" IS NULL)");
        });
        builder.HasKey(item => item.Id);
        builder.HasIndex(item => item.SourceMessageId).IsUnique().HasDatabaseName("ux_work_items_source_message_id");
        builder.HasIndex(item => new { item.Status, item.CreatedAt }).HasDatabaseName("ix_work_items_status_created_at");
        builder.Property(item => item.Status).HasMaxLength(32).IsRequired();
        builder.Property(item => item.AssignedReviewerId).HasMaxLength(128);
        builder.Property(item => item.EffectiveCategory).HasMaxLength(32).IsRequired();
        builder.Property(item => item.EffectivePriority).HasMaxLength(32).IsRequired();
        builder.Property(item => item.ReviewerNotes).HasMaxLength(SmartInbox.Domain.WorkItems.WorkItem.MaximumNotesLength);
        builder.Property(item => item.MissingInformationJson).HasColumnType("jsonb").IsRequired();
        builder.Property(item => item.ResolvedBy).HasMaxLength(128);
        builder.Property(item => item.Version).IsConcurrencyToken();
        builder.HasOne(item => item.SourceMessage).WithOne()
            .HasForeignKey<WorkItemRecord>(item => item.SourceMessageId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(item => item.Facts).WithOne().HasForeignKey(fact => fact.WorkItemId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.Draft).WithOne().HasForeignKey<ResponseDraftRecord>(draft => draft.WorkItemId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(item => item.AuditEntries).WithOne().HasForeignKey(audit => audit.WorkItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
