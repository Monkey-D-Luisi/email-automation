using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartInbox.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inbound_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MailboxId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ExternalMessageId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ExternalThreadId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SenderEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    SenderName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Subject = table.Column<string>(type: "character varying(998)", maxLength: 998, nullable: false),
                    BodyText = table.Column<string>(type: "character varying(40000)", maxLength: 40000, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ImportedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ContentFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AttachmentsJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbound_messages", x => x.Id);
                    table.CheckConstraint("ck_inbound_messages_content_fingerprint_length", "char_length(\"ContentFingerprint\") = 64");
                    table.CheckConstraint("ck_inbound_messages_status", "\"Status\" IN ('Received', 'Processing', 'Processed', 'Failed')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_inbound_messages_imported_at",
                table: "inbound_messages",
                column: "ImportedAt");

            migrationBuilder.CreateIndex(
                name: "ux_inbound_messages_source_mailbox_external_message_id",
                table: "inbound_messages",
                columns: new[] { "Source", "MailboxId", "ExternalMessageId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbound_messages");
        }
    }
}
