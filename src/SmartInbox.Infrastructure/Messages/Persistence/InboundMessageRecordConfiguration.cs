using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartInbox.Infrastructure.Messages.Persistence;

internal sealed class InboundMessageRecordConfiguration : IEntityTypeConfiguration<InboundMessageRecord>
{
    public const string IdempotencyIndexName = "ux_inbound_messages_source_mailbox_external_message_id";

    public void Configure(EntityTypeBuilder<InboundMessageRecord> builder)
    {
        builder.ToTable("inbound_messages", table =>
        {
            table.HasCheckConstraint("ck_inbound_messages_status", "\"Status\" IN ('Received', 'Processing', 'Processed', 'Failed')");
            table.HasCheckConstraint("ck_inbound_messages_content_fingerprint_length", "char_length(\"ContentFingerprint\") = 64");
            table.HasCheckConstraint("ck_inbound_messages_processing_attempt_count", "\"ProcessingAttemptCount\" >= 0");
            table.HasCheckConstraint("ck_inbound_messages_lease_state", "(\"Status\" = 'Processing' AND \"ProcessingLeaseOwner\" IS NOT NULL AND \"ProcessingLeaseExpiresAt\" IS NOT NULL) OR (\"Status\" <> 'Processing' AND \"ProcessingLeaseOwner\" IS NULL AND \"ProcessingLeaseExpiresAt\" IS NULL)");
            table.HasCheckConstraint("ck_inbound_messages_failure_state", "(\"Status\" = 'Failed' AND \"FailureKind\" IS NOT NULL AND \"FailureMessage\" IS NOT NULL AND ((\"ProcessingRetryable\" AND \"NextAttemptAt\" IS NOT NULL) OR (NOT \"ProcessingRetryable\" AND \"NextAttemptAt\" IS NULL))) OR (\"Status\" <> 'Failed' AND \"FailureKind\" IS NULL AND \"FailureMessage\" IS NULL AND NOT \"ProcessingRetryable\" AND \"NextAttemptAt\" IS NULL)");
        });

        builder.HasKey(message => message.Id);
        builder.Property(message => message.Source).HasMaxLength(64).IsRequired();
        builder.Property(message => message.MailboxId).HasMaxLength(256).IsRequired();
        builder.Property(message => message.ExternalMessageId).HasMaxLength(512).IsRequired();
        builder.Property(message => message.ExternalThreadId).HasMaxLength(512);
        builder.Property(message => message.SenderEmail).HasMaxLength(320);
        builder.Property(message => message.SenderName).HasMaxLength(256);
        builder.Property(message => message.Subject).HasMaxLength(998).IsRequired();
        builder.Property(message => message.BodyText).HasMaxLength(40_000).IsRequired();
        builder.Property(message => message.ReceivedAt).HasColumnType("timestamp with time zone");
        builder.Property(message => message.ImportedAt).HasColumnType("timestamp with time zone");
        builder.Property(message => message.Status).HasMaxLength(32).IsRequired();
        builder.Property(message => message.ContentFingerprint).HasMaxLength(64).IsRequired();
        builder.Property(message => message.AttachmentsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(message => message.ProcessingAttemptCount).HasDefaultValue(0);
        builder.Property(message => message.FailureKind).HasMaxLength(64);
        builder.Property(message => message.FailureMessage).HasMaxLength(250);
        builder.Property(message => message.NextAttemptAt).HasColumnType("timestamp with time zone");
        builder.Property(message => message.ProcessingLeaseExpiresAt).HasColumnType("timestamp with time zone");
        builder.Property(message => message.LastProviderId).HasMaxLength(64);
        builder.Property(message => message.LastModel).HasMaxLength(128);
        builder.Property(message => message.LastEstimatedCostUsd).HasPrecision(18, 8);

        builder.HasIndex(message => new { message.Source, message.MailboxId, message.ExternalMessageId })
            .IsUnique()
            .HasDatabaseName(IdempotencyIndexName);
        builder.HasIndex(message => message.ImportedAt).HasDatabaseName("ix_inbound_messages_imported_at");
        builder.HasIndex(message => new { message.Status, message.NextAttemptAt, message.ProcessingLeaseExpiresAt })
            .HasDatabaseName("ix_inbound_messages_processing_queue");
        builder.HasOne(message => message.Classification)
            .WithOne(classification => classification.Message)
            .HasForeignKey<EmailClassificationRecord>(classification => classification.MessageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
