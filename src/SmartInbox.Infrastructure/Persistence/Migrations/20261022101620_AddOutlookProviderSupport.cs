using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartInbox.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutlookProviderSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_gmail_draft_exports_draft_revision_destination_recipient",
                table: "gmail_draft_exports");

            migrationBuilder.AddColumn<string>(
                name: "ExternalInternetMessageId",
                table: "inbound_messages",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalRecipientEmailsJson",
                table: "inbound_messages",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "gmail_draft_exports",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Gmail");

            migrationBuilder.CreateIndex(
                name: "ux_gmail_draft_exports_provider_draft_revision_destination_recipient",
                table: "gmail_draft_exports",
                columns: new[] { "Provider", "DraftId", "ApprovedRevision", "DestinationMailboxId", "RecipientEmail" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_gmail_draft_exports_provider",
                table: "gmail_draft_exports",
                sql: "\"Provider\" IN ('Gmail', 'Outlook')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_gmail_draft_exports_provider_draft_revision_destination_recipient",
                table: "gmail_draft_exports");

            migrationBuilder.DropCheckConstraint(
                name: "ck_gmail_draft_exports_provider",
                table: "gmail_draft_exports");

            migrationBuilder.DropColumn(
                name: "ExternalInternetMessageId",
                table: "inbound_messages");

            migrationBuilder.DropColumn(
                name: "ExternalRecipientEmailsJson",
                table: "inbound_messages");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "gmail_draft_exports");

            migrationBuilder.CreateIndex(
                name: "ux_gmail_draft_exports_draft_revision_destination_recipient",
                table: "gmail_draft_exports",
                columns: new[] { "DraftId", "ApprovedRevision", "DestinationMailboxId", "RecipientEmail" },
                unique: true);
        }
    }
}
