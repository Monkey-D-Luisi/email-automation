using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartInbox.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHumanReviewAndDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "work_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceMessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AssignedReviewerId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    EffectiveCategory = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EffectivePriority = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReviewerNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    MissingInformationJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResolvedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_items", x => x.Id);
                    table.CheckConstraint("ck_work_items_assignment", "(\"Status\" = 'PendingReview' AND \"AssignedReviewerId\" IS NULL) OR (\"Status\" <> 'PendingReview' AND \"AssignedReviewerId\" IS NOT NULL)");
                    table.CheckConstraint("ck_work_items_category", "\"EffectiveCategory\" IN ('SalesInquiry', 'SupportRequest', 'BillingDocument', 'GeneralInquiry', 'Irrelevant', 'Unknown')");
                    table.CheckConstraint("ck_work_items_priority", "\"EffectivePriority\" IN ('Low', 'Normal', 'High', 'Urgent')");
                    table.CheckConstraint("ck_work_items_resolution", "(\"Status\" = 'Resolved' AND \"ResolvedAt\" IS NOT NULL AND \"ResolvedBy\" IS NOT NULL) OR (\"Status\" <> 'Resolved' AND \"ResolvedAt\" IS NULL AND \"ResolvedBy\" IS NULL)");
                    table.CheckConstraint("ck_work_items_status", "\"Status\" IN ('PendingReview', 'InReview', 'Resolved')");
                    table.CheckConstraint("ck_work_items_version", "\"Version\" >= 0");
                    table.ForeignKey(
                        name: "FK_work_items_inbound_messages_SourceMessageId",
                        column: x => x.SourceMessageId,
                        principalTable: "inbound_messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "response_drafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_response_drafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_response_drafts_work_items_WorkItemId",
                        column: x => x.WorkItemId,
                        principalTable: "work_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "work_item_audit_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    Revision = table.Column<int>(type: "integer", nullable: true),
                    Summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_item_audit_entries", x => x.Id);
                    table.CheckConstraint("ck_work_item_audit_action", "\"Action\" IN ('Created', 'ReviewStarted', 'CategoryCorrected', 'PriorityCorrected', 'NotesChanged', 'MissingInformationChanged', 'FactVerified', 'FactCorrected', 'FactRejected', 'DraftCreated', 'DraftEdited', 'DraftApproved', 'DraftRejected', 'Resolved')");
                    table.ForeignKey(
                        name: "FK_work_item_audit_entries_work_items_WorkItemId",
                        column: x => x.WorkItemId,
                        principalTable: "work_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "work_item_facts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OriginalValue = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Value = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_item_facts", x => x.Id);
                    table.CheckConstraint("ck_work_item_facts_modified", "(\"ModifiedAt\" IS NULL) = (\"ModifiedBy\" IS NULL)");
                    table.CheckConstraint("ck_work_item_facts_status", "\"Status\" IN ('Unverified', 'Verified', 'Rejected')");
                    table.CheckConstraint("ck_work_item_facts_type", "\"Type\" IN ('CustomerName', 'CompanyName', 'ProductOrService', 'InvoiceNumber', 'Amount', 'Currency', 'OrderReference', 'RequestedDate', 'ContactDetails', 'Address', 'Other')");
                    table.CheckConstraint("ck_work_item_facts_value", "char_length(\"OriginalValue\") BETWEEN 1 AND 300 AND char_length(\"Value\") BETWEEN 1 AND 300");
                    table.ForeignKey(
                        name: "FK_work_item_facts_work_items_WorkItemId",
                        column: x => x.WorkItemId,
                        principalTable: "work_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "response_draft_revisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResponseDraftId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    Subject = table.Column<string>(type: "character varying(998)", maxLength: 998, nullable: false),
                    Body = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecidedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Feedback = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ProviderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    DurationMilliseconds = table.Column<long>(type: "bigint", nullable: true),
                    InputTokens = table.Column<int>(type: "integer", nullable: true),
                    CachedInputTokens = table.Column<int>(type: "integer", nullable: true),
                    OutputTokens = table.Column<int>(type: "integer", nullable: true),
                    EstimatedCostUsd = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_response_draft_revisions", x => x.Id);
                    table.CheckConstraint("ck_response_draft_revisions_content", "char_length(\"Subject\") BETWEEN 1 AND 998 AND char_length(\"Body\") BETWEEN 1 AND 20000");
                    table.CheckConstraint("ck_response_draft_revisions_decision", "(\"Status\" = 'Draft' AND \"DecidedAt\" IS NULL AND \"DecidedBy\" IS NULL) OR (\"Status\" <> 'Draft' AND \"DecidedAt\" IS NOT NULL AND \"DecidedBy\" IS NOT NULL)");
                    table.CheckConstraint("ck_response_draft_revisions_revision", "\"Revision\" > 0");
                    table.CheckConstraint("ck_response_draft_revisions_status", "\"Status\" IN ('Draft', 'Approved', 'Rejected')");
                    table.ForeignKey(
                        name: "FK_response_draft_revisions_response_drafts_ResponseDraftId",
                        column: x => x.ResponseDraftId,
                        principalTable: "response_drafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_response_draft_revisions_draft_revision",
                table: "response_draft_revisions",
                columns: new[] { "ResponseDraftId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_response_drafts_work_item_id",
                table: "response_drafts",
                column: "WorkItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_work_item_audit_work_item_occurred_at",
                table: "work_item_audit_entries",
                columns: new[] { "WorkItemId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "ix_work_item_facts_work_item_id",
                table: "work_item_facts",
                column: "WorkItemId");

            migrationBuilder.CreateIndex(
                name: "ix_work_items_status_created_at",
                table: "work_items",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "ux_work_items_source_message_id",
                table: "work_items",
                column: "SourceMessageId",
                unique: true);

            migrationBuilder.Sql("""
                WITH created AS (
                    INSERT INTO work_items (
                        "Id", "SourceMessageId", "Status", "AssignedReviewerId", "EffectiveCategory",
                        "EffectivePriority", "ReviewerNotes", "MissingInformationJson", "CreatedAt", "UpdatedAt",
                        "ResolvedAt", "ResolvedBy", "Version")
                    SELECT gen_random_uuid(), message."Id", 'PendingReview', NULL, classification."Category",
                        classification."Priority", NULL, classification."MissingInformationJson",
                        classification."ClassifiedAt", classification."ClassifiedAt", NULL, NULL, 0
                    FROM inbound_messages AS message
                    INNER JOIN email_classifications AS classification ON classification."MessageId" = message."Id"
                    WHERE message."Status" = 'Processed'
                    ON CONFLICT ("SourceMessageId") DO NOTHING
                    RETURNING "Id", "SourceMessageId", "CreatedAt"
                ), audit AS (
                    INSERT INTO work_item_audit_entries
                        ("Id", "WorkItemId", "ActorId", "Action", "OccurredAt", "EntityId", "Revision", "Summary")
                    SELECT gen_random_uuid(), created."Id", 'system', 'Created', created."CreatedAt", NULL, NULL,
                        'Work item backfilled from a completed classification.'
                    FROM created
                )
                INSERT INTO work_item_facts
                    ("Id", "WorkItemId", "Type", "OriginalValue", "Value", "Status", "ModifiedAt", "ModifiedBy")
                SELECT gen_random_uuid(), created."Id", COALESCE(fact->>'type', fact->>'Type'),
                    COALESCE(fact->>'value', fact->>'Value'), COALESCE(fact->>'value', fact->>'Value'),
                    'Unverified', NULL, NULL
                FROM created
                INNER JOIN email_classifications AS classification ON classification."MessageId" = created."SourceMessageId"
                CROSS JOIN LATERAL jsonb_array_elements(classification."FactsJson") AS fact
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "response_draft_revisions");

            migrationBuilder.DropTable(
                name: "work_item_audit_entries");

            migrationBuilder.DropTable(
                name: "work_item_facts");

            migrationBuilder.DropTable(
                name: "response_drafts");

            migrationBuilder.DropTable(
                name: "work_items");
        }
    }
}
