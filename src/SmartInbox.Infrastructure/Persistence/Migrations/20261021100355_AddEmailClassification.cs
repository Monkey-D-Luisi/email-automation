using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartInbox.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailClassification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FailureKind",
                table: "inbound_messages",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FailureMessage",
                table: "inbound_messages",
                type: "character varying(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastCachedInputTokens",
                table: "inbound_messages",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LastDurationMilliseconds",
                table: "inbound_messages",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LastEstimatedCostUsd",
                table: "inbound_messages",
                type: "numeric(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastInputTokens",
                table: "inbound_messages",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastModel",
                table: "inbound_messages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastOutputTokens",
                table: "inbound_messages",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastProviderId",
                table: "inbound_messages",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextAttemptAt",
                table: "inbound_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProcessingAttemptCount",
                table: "inbound_messages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProcessingLeaseExpiresAt",
                table: "inbound_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProcessingLeaseOwner",
                table: "inbound_messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ProcessingRetryable",
                table: "inbound_messages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "email_classifications",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Priority = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Language = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    FactsJson = table.Column<string>(type: "jsonb", nullable: false),
                    MissingInformationJson = table.Column<string>(type: "jsonb", nullable: false),
                    WarningsJson = table.Column<string>(type: "jsonb", nullable: false),
                    UncertaintyReasonsJson = table.Column<string>(type: "jsonb", nullable: false),
                    ReviewReasonsJson = table.Column<string>(type: "jsonb", nullable: false),
                    RequiresReview = table.Column<bool>(type: "boolean", nullable: false),
                    ClassifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_classifications", x => x.MessageId);
                    table.CheckConstraint("ck_email_classifications_category", "\"Category\" IN ('SalesInquiry', 'SupportRequest', 'BillingDocument', 'GeneralInquiry', 'Irrelevant', 'Unknown')");
                    table.CheckConstraint("ck_email_classifications_priority", "\"Priority\" IN ('Low', 'Normal', 'High', 'Urgent')");
                    table.CheckConstraint("ck_email_classifications_review", "\"RequiresReview\" = (jsonb_array_length(\"ReviewReasonsJson\") > 0)");
                    table.ForeignKey(
                        name: "FK_email_classifications_inbound_messages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "inbound_messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_inbound_messages_processing_queue",
                table: "inbound_messages",
                columns: new[] { "Status", "NextAttemptAt", "ProcessingLeaseExpiresAt" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_inbound_messages_failure_state",
                table: "inbound_messages",
                sql: "(\"Status\" = 'Failed' AND \"FailureKind\" IS NOT NULL AND \"FailureMessage\" IS NOT NULL AND ((\"ProcessingRetryable\" AND \"NextAttemptAt\" IS NOT NULL) OR (NOT \"ProcessingRetryable\" AND \"NextAttemptAt\" IS NULL))) OR (\"Status\" <> 'Failed' AND \"FailureKind\" IS NULL AND \"FailureMessage\" IS NULL AND NOT \"ProcessingRetryable\" AND \"NextAttemptAt\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_inbound_messages_lease_state",
                table: "inbound_messages",
                sql: "(\"Status\" = 'Processing' AND \"ProcessingLeaseOwner\" IS NOT NULL AND \"ProcessingLeaseExpiresAt\" IS NOT NULL) OR (\"Status\" <> 'Processing' AND \"ProcessingLeaseOwner\" IS NULL AND \"ProcessingLeaseExpiresAt\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_inbound_messages_processing_attempt_count",
                table: "inbound_messages",
                sql: "\"ProcessingAttemptCount\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "email_classifications");

            migrationBuilder.DropIndex(
                name: "ix_inbound_messages_processing_queue",
                table: "inbound_messages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_inbound_messages_failure_state",
                table: "inbound_messages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_inbound_messages_lease_state",
                table: "inbound_messages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_inbound_messages_processing_attempt_count",
                table: "inbound_messages");

            migrationBuilder.DropColumn(
                name: "FailureKind",
                table: "inbound_messages");

            migrationBuilder.DropColumn(
                name: "FailureMessage",
                table: "inbound_messages");

            migrationBuilder.DropColumn(
                name: "LastCachedInputTokens",
                table: "inbound_messages");

            migrationBuilder.DropColumn(
                name: "LastDurationMilliseconds",
                table: "inbound_messages");

            migrationBuilder.DropColumn(
                name: "LastEstimatedCostUsd",
                table: "inbound_messages");

            migrationBuilder.DropColumn(
                name: "LastInputTokens",
                table: "inbound_messages");

            migrationBuilder.DropColumn(
                name: "LastModel",
                table: "inbound_messages");

            migrationBuilder.DropColumn(
                name: "LastOutputTokens",
                table: "inbound_messages");

            migrationBuilder.DropColumn(
                name: "LastProviderId",
                table: "inbound_messages");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "inbound_messages");

            migrationBuilder.DropColumn(
                name: "ProcessingAttemptCount",
                table: "inbound_messages");

            migrationBuilder.DropColumn(
                name: "ProcessingLeaseExpiresAt",
                table: "inbound_messages");

            migrationBuilder.DropColumn(
                name: "ProcessingLeaseOwner",
                table: "inbound_messages");

            migrationBuilder.DropColumn(
                name: "ProcessingRetryable",
                table: "inbound_messages");
        }
    }
}
