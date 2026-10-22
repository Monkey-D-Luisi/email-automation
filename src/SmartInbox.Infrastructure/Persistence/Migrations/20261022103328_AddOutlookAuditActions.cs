using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartInbox.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutlookAuditActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_work_item_audit_action",
                table: "work_item_audit_entries");

            migrationBuilder.AddCheckConstraint(
                name: "ck_work_item_audit_action",
                table: "work_item_audit_entries",
                sql: "\"Action\" IN ('Created', 'ReviewStarted', 'CategoryCorrected', 'PriorityCorrected', 'NotesChanged', 'MissingInformationChanged', 'FactVerified', 'FactCorrected', 'FactRejected', 'DraftCreated', 'DraftEdited', 'DraftApproved', 'DraftRejected', 'GmailDraftExportRequested', 'GmailDraftExportClaimed', 'GmailDraftExportSucceeded', 'GmailDraftExportFailed', 'GmailDraftExportNeedsReconciliation', 'GmailDraftExportCancelled', 'GmailDraftExportRetryAuthorized', 'GmailDraftExportReconciled', 'OutlookDraftExportRequested', 'OutlookDraftExportClaimed', 'OutlookDraftExportSucceeded', 'OutlookDraftExportFailed', 'OutlookDraftExportNeedsReconciliation', 'OutlookDraftExportCancelled', 'OutlookDraftExportRetryAuthorized', 'OutlookDraftExportReconciled', 'Resolved')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_work_item_audit_action",
                table: "work_item_audit_entries");

            migrationBuilder.AddCheckConstraint(
                name: "ck_work_item_audit_action",
                table: "work_item_audit_entries",
                sql: "\"Action\" IN ('Created', 'ReviewStarted', 'CategoryCorrected', 'PriorityCorrected', 'NotesChanged', 'MissingInformationChanged', 'FactVerified', 'FactCorrected', 'FactRejected', 'DraftCreated', 'DraftEdited', 'DraftApproved', 'DraftRejected', 'GmailDraftExportRequested', 'GmailDraftExportClaimed', 'GmailDraftExportSucceeded', 'GmailDraftExportFailed', 'GmailDraftExportNeedsReconciliation', 'GmailDraftExportCancelled', 'GmailDraftExportRetryAuthorized', 'GmailDraftExportReconciled', 'Resolved')");
        }
    }
}
