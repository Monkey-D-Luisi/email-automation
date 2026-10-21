using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartInbox.Infrastructure.Persistence;

internal sealed class InboundMessageRecordConfiguration : IEntityTypeConfiguration<InboundMessageRecord>
{
    public const string IdempotencyIndexName = "ux_inbound_messages_source_mailbox_external_message_id";

    public void Configure(EntityTypeBuilder<InboundMessageRecord> builder)
    {
        builder.ToTable("inbound_messages", table =>
        {
            table.HasCheckConstraint("ck_inbound_messages_status", "\"Status\" IN ('Received', 'Processing', 'Processed', 'Failed')");
            table.HasCheckConstraint("ck_inbound_messages_content_fingerprint_length", "char_length(\"ContentFingerprint\") = 64");
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

        builder.HasIndex(message => new { message.Source, message.MailboxId, message.ExternalMessageId })
            .IsUnique()
            .HasDatabaseName(IdempotencyIndexName);
        builder.HasIndex(message => message.ImportedAt).HasDatabaseName("ix_inbound_messages_imported_at");
    }
}
