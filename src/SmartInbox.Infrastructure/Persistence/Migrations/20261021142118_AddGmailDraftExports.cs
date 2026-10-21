using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartInbox.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGmailDraftExports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_work_item_audit_action",
                table: "work_item_audit_entries");

            migrationBuilder.CreateTable(
                name: "gmail_draft_exports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    DraftId = table.Column<Guid>(type: "uuid", nullable: false),
                    DraftRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovedRevision = table.Column<int>(type: "integer", nullable: false),
                    DestinationMailboxId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RecipientEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Subject = table.Column<string>(type: "character varying(998)", maxLength: 998, nullable: false),
                    Body = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    ContentFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClaimedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    GmailDraftId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    FailureKind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gmail_draft_exports", x => x.Id);
                    table.CheckConstraint("ck_gmail_draft_exports_attempt_count", "\"AttemptCount\" >= 0");
                    table.CheckConstraint("ck_gmail_draft_exports_content", "char_length(\"Subject\") BETWEEN 1 AND 998 AND char_length(\"Body\") BETWEEN 1 AND 20000");
                    table.CheckConstraint("ck_gmail_draft_exports_fingerprint", "\"ContentFingerprint\" ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_gmail_draft_exports_revision", "\"ApprovedRevision\" > 0");
                    table.CheckConstraint("ck_gmail_draft_exports_state", "(\"Status\" = 'Pending' AND \"ClaimedAt\" IS NULL AND \"LeaseExpiresAt\" IS NULL AND \"CompletedAt\" IS NULL AND \"GmailDraftId\" IS NULL AND \"FailureKind\" IS NULL) OR (\"Status\" = 'InProgress' AND \"AttemptId\" IS NOT NULL AND \"ClaimedAt\" IS NOT NULL AND \"LeaseExpiresAt\" IS NOT NULL AND \"CompletedAt\" IS NULL AND \"GmailDraftId\" IS NULL AND \"FailureKind\" IS NULL) OR (\"Status\" = 'Succeeded' AND \"AttemptId\" IS NOT NULL AND \"ClaimedAt\" IS NULL AND \"LeaseExpiresAt\" IS NULL AND \"CompletedAt\" IS NOT NULL AND \"GmailDraftId\" IS NOT NULL AND \"FailureKind\" IS NULL) OR (\"Status\" IN ('Failed', 'NeedsReconciliation') AND \"AttemptId\" IS NOT NULL AND \"ClaimedAt\" IS NULL AND \"LeaseExpiresAt\" IS NULL AND \"CompletedAt\" IS NOT NULL AND \"GmailDraftId\" IS NULL AND \"FailureKind\" IS NOT NULL) OR (\"Status\" = 'Cancelled' AND \"ClaimedAt\" IS NULL AND \"LeaseExpiresAt\" IS NULL AND \"CompletedAt\" IS NOT NULL AND \"GmailDraftId\" IS NULL AND \"FailureKind\" IS NULL)");
                    table.CheckConstraint("ck_gmail_draft_exports_status", "\"Status\" IN ('Pending', 'InProgress', 'Succeeded', 'Failed', 'NeedsReconciliation', 'Cancelled')");
                    table.ForeignKey(
                        name: "FK_gmail_draft_exports_work_items_WorkItemId",
                        column: x => x.WorkItemId,
                        principalTable: "work_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_work_item_audit_action",
                table: "work_item_audit_entries",
                sql: "\"Action\" IN ('Created', 'ReviewStarted', 'CategoryCorrected', 'PriorityCorrected', 'NotesChanged', 'MissingInformationChanged', 'FactVerified', 'FactCorrected', 'FactRejected', 'DraftCreated', 'DraftEdited', 'DraftApproved', 'DraftRejected', 'GmailDraftExportRequested', 'GmailDraftExportClaimed', 'GmailDraftExportSucceeded', 'GmailDraftExportFailed', 'GmailDraftExportNeedsReconciliation', 'GmailDraftExportCancelled', 'GmailDraftExportRetryAuthorized', 'GmailDraftExportReconciled', 'Resolved')");

            migrationBuilder.CreateIndex(
                name: "ix_gmail_draft_exports_status_created_at",
                table: "gmail_draft_exports",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "ix_gmail_draft_exports_work_item_created_at",
                table: "gmail_draft_exports",
                columns: new[] { "WorkItemId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "ux_gmail_draft_exports_draft_revision_destination_recipient",
                table: "gmail_draft_exports",
                columns: new[] { "DraftId", "ApprovedRevision", "DestinationMailboxId", "RecipientEmail" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gmail_draft_exports");

            migrationBuilder.DropCheckConstraint(
                name: "ck_work_item_audit_action",
                table: "work_item_audit_entries");

            migrationBuilder.AddCheckConstraint(
                name: "ck_work_item_audit_action",
                table: "work_item_audit_entries",
                sql: "\"Action\" IN ('Created', 'ReviewStarted', 'CategoryCorrected', 'PriorityCorrected', 'NotesChanged', 'MissingInformationChanged', 'FactVerified', 'FactCorrected', 'FactRejected', 'DraftCreated', 'DraftEdited', 'DraftApproved', 'DraftRejected', 'Resolved')");
        }
    }
}
