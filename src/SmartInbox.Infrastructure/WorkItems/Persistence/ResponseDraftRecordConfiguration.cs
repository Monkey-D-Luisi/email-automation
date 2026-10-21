using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartInbox.Infrastructure.WorkItems.Persistence;

internal sealed class ResponseDraftRecordConfiguration : IEntityTypeConfiguration<ResponseDraftRecord>
{
    public void Configure(EntityTypeBuilder<ResponseDraftRecord> builder)
    {
        builder.ToTable("response_drafts");
        builder.HasKey(draft => draft.Id);
        builder.HasIndex(draft => draft.WorkItemId).IsUnique().HasDatabaseName("ux_response_drafts_work_item_id");
        builder.Property(draft => draft.RecipientEmail).HasMaxLength(320).IsRequired();
        builder.HasMany(draft => draft.Revisions).WithOne().HasForeignKey(revision => revision.ResponseDraftId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
