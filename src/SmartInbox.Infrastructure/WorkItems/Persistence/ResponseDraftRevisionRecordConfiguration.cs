using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartInbox.Infrastructure.WorkItems.Persistence;

internal sealed class ResponseDraftRevisionRecordConfiguration : IEntityTypeConfiguration<ResponseDraftRevisionRecord>
{
    public void Configure(EntityTypeBuilder<ResponseDraftRevisionRecord> builder)
    {
        builder.ToTable("response_draft_revisions", table =>
        {
            table.HasCheckConstraint("ck_response_draft_revisions_status", "\"Status\" IN ('Draft', 'Approved', 'Rejected')");
            table.HasCheckConstraint("ck_response_draft_revisions_revision", "\"Revision\" > 0");
            table.HasCheckConstraint("ck_response_draft_revisions_content", "char_length(\"Subject\") BETWEEN 1 AND 998 AND char_length(\"Body\") BETWEEN 1 AND 20000");
            table.HasCheckConstraint("ck_response_draft_revisions_decision", "(\"Status\" = 'Draft' AND \"DecidedAt\" IS NULL AND \"DecidedBy\" IS NULL) OR (\"Status\" <> 'Draft' AND \"DecidedAt\" IS NOT NULL AND \"DecidedBy\" IS NOT NULL)");
        });
        builder.HasKey(revision => revision.Id);
        builder.HasIndex(revision => new { revision.ResponseDraftId, revision.Revision }).IsUnique()
            .HasDatabaseName("ux_response_draft_revisions_draft_revision");
        builder.Property(revision => revision.Subject).HasMaxLength(998).IsRequired();
        builder.Property(revision => revision.Body).HasMaxLength(20_000).IsRequired();
        builder.Property(revision => revision.Status).HasMaxLength(32).IsRequired();
        builder.Property(revision => revision.CreatedBy).HasMaxLength(128).IsRequired();
        builder.Property(revision => revision.DecidedBy).HasMaxLength(128);
        builder.Property(revision => revision.Feedback).HasMaxLength(500);
        builder.Property(revision => revision.ProviderId).HasMaxLength(64);
        builder.Property(revision => revision.Model).HasMaxLength(128);
        builder.Property(revision => revision.EstimatedCostUsd).HasPrecision(18, 8);
    }
}
