using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartInbox.Infrastructure.Messages.Persistence;

internal sealed class EmailClassificationRecordConfiguration : IEntityTypeConfiguration<EmailClassificationRecord>
{
    public void Configure(EntityTypeBuilder<EmailClassificationRecord> builder)
    {
        builder.ToTable("email_classifications", table =>
        {
            table.HasCheckConstraint("ck_email_classifications_category", "\"Category\" IN ('SalesInquiry', 'SupportRequest', 'BillingDocument', 'GeneralInquiry', 'Irrelevant', 'Unknown')");
            table.HasCheckConstraint("ck_email_classifications_priority", "\"Priority\" IN ('Low', 'Normal', 'High', 'Urgent')");
            table.HasCheckConstraint("ck_email_classifications_review", "\"RequiresReview\" = (jsonb_array_length(\"ReviewReasonsJson\") > 0)");
        });

        builder.HasKey(classification => classification.MessageId);
        builder.Property(classification => classification.Category).HasMaxLength(32).IsRequired();
        builder.Property(classification => classification.Priority).HasMaxLength(32).IsRequired();
        builder.Property(classification => classification.Summary).HasMaxLength(1_000).IsRequired();
        builder.Property(classification => classification.Language).HasMaxLength(16).IsRequired();
        builder.Property(classification => classification.FactsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(classification => classification.MissingInformationJson).HasColumnType("jsonb").IsRequired();
        builder.Property(classification => classification.WarningsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(classification => classification.UncertaintyReasonsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(classification => classification.ReviewReasonsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(classification => classification.ClassifiedAt).HasColumnType("timestamp with time zone");
    }
}
