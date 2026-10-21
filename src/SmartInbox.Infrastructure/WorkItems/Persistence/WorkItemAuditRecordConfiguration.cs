using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartInbox.Infrastructure.WorkItems.Persistence;

internal sealed class WorkItemAuditRecordConfiguration : IEntityTypeConfiguration<WorkItemAuditRecord>
{
    public void Configure(EntityTypeBuilder<WorkItemAuditRecord> builder)
    {
        builder.ToTable("work_item_audit_entries", table =>
            table.HasCheckConstraint("ck_work_item_audit_action", "\"Action\" IN ('Created', 'ReviewStarted', 'CategoryCorrected', 'PriorityCorrected', 'NotesChanged', 'MissingInformationChanged', 'FactVerified', 'FactCorrected', 'FactRejected', 'DraftCreated', 'DraftEdited', 'DraftApproved', 'DraftRejected', 'Resolved')"));
        builder.HasKey(entry => entry.Id);
        builder.HasIndex(entry => new { entry.WorkItemId, entry.OccurredAt }).HasDatabaseName("ix_work_item_audit_work_item_occurred_at");
        builder.Property(entry => entry.ActorId).HasMaxLength(128).IsRequired();
        builder.Property(entry => entry.Action).HasMaxLength(64).IsRequired();
        builder.Property(entry => entry.Summary).HasMaxLength(500).IsRequired();
    }
}
