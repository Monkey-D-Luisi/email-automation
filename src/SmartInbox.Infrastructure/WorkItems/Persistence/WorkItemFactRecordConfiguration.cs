using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartInbox.Infrastructure.WorkItems.Persistence;

internal sealed class WorkItemFactRecordConfiguration : IEntityTypeConfiguration<WorkItemFactRecord>
{
    public void Configure(EntityTypeBuilder<WorkItemFactRecord> builder)
    {
        builder.ToTable("work_item_facts", table =>
        {
            table.HasCheckConstraint("ck_work_item_facts_type", "\"Type\" IN ('CustomerName', 'CompanyName', 'ProductOrService', 'InvoiceNumber', 'Amount', 'Currency', 'OrderReference', 'RequestedDate', 'ContactDetails', 'Address', 'Other')");
            table.HasCheckConstraint("ck_work_item_facts_status", "\"Status\" IN ('Unverified', 'Verified', 'Rejected')");
            table.HasCheckConstraint("ck_work_item_facts_value", "char_length(\"OriginalValue\") BETWEEN 1 AND 300 AND char_length(\"Value\") BETWEEN 1 AND 300");
            table.HasCheckConstraint("ck_work_item_facts_modified", "(\"ModifiedAt\" IS NULL) = (\"ModifiedBy\" IS NULL)");
        });
        builder.HasKey(fact => fact.Id);
        builder.HasIndex(fact => fact.WorkItemId).HasDatabaseName("ix_work_item_facts_work_item_id");
        builder.Property(fact => fact.Type).HasMaxLength(32).IsRequired();
        builder.Property(fact => fact.OriginalValue).HasMaxLength(300).IsRequired();
        builder.Property(fact => fact.Value).HasMaxLength(300).IsRequired();
        builder.Property(fact => fact.Status).HasMaxLength(32).IsRequired();
        builder.Property(fact => fact.ModifiedBy).HasMaxLength(128);
    }
}
