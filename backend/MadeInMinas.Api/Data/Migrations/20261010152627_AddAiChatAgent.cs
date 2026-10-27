using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MadeInMinas.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAiChatAgent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ChatMessages_Author",
                table: "ChatMessages");

            migrationBuilder.AddColumn<DateOnly>(
                name: "AiCallsDate",
                table: "ChatConversations",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AiDailyCalls",
                table: "ChatConversations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AiLeaseExpiresAt",
                table: "ChatConversations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AiLeaseId",
                table: "ChatConversations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AiLeaseSourceSequence",
                table: "ChatConversations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AiProcessedSequence",
                table: "ChatConversations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AiTotalCalls",
                table: "ChatConversations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "AutomationMode",
                table: "ChatConversations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Manual");

            migrationBuilder.CreateTable(
                name: "ChatOrderDrafts",
                columns: table => new
                {
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ItemsJson = table.Column<string>(type: "jsonb", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Fulfillment = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    AddressJson = table.Column<string>(type: "jsonb", nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    QuoteJson = table.Column<string>(type: "jsonb", nullable: true),
                    ReviewJson = table.Column<string>(type: "jsonb", nullable: true),
                    OrderRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConfirmationRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConfirmationHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReceiptJson = table.Column<string>(type: "jsonb", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatOrderDrafts", x => x.ConversationId);
                    table.CheckConstraint("CK_ChatOrderDrafts_Fulfillment", "\"Fulfillment\" IS NULL OR \"Fulfillment\" IN ('Pickup','Delivery')");
                    table.CheckConstraint("CK_ChatOrderDrafts_Receipt", "(\"ReceiptJson\" IS NULL AND \"OrderId\" IS NULL AND \"ConfirmationRequestId\" IS NULL AND \"ConfirmationHash\" IS NULL) OR (\"ReceiptJson\" IS NOT NULL AND \"OrderId\" IS NOT NULL AND \"OrderRequestId\" IS NOT NULL AND \"ConfirmationRequestId\" IS NOT NULL AND \"ConfirmationHash\" IS NOT NULL)");
                    table.CheckConstraint("CK_ChatOrderDrafts_Version", "\"Version\" >= 1");
                    table.ForeignKey(
                        name: "FK_ChatOrderDrafts_ChatConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "ChatConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChatOrderDrafts_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ChatMessages_Author",
                table: "ChatMessages",
                sql: "(\"Kind\" = 'Visitor' AND \"ActorId\" IS NULL AND \"ActorName\" IS NULL AND \"RequestId\" IS NOT NULL) OR (\"Kind\" = 'Staff' AND \"ActorId\" IS NOT NULL AND \"ActorName\" IS NOT NULL AND \"RequestId\" IS NOT NULL) OR (\"Kind\" = 'Assistant' AND \"ActorId\" IS NULL AND \"ActorName\" IS NULL AND \"RequestId\" IS NULL) OR (\"Kind\" = 'System' AND \"RequestId\" IS NULL AND ((\"ActorId\" IS NOT NULL AND \"ActorName\" IS NOT NULL) OR (\"ActorId\" IS NULL AND \"ActorName\" IS NULL)))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ChatConversations_AiLease",
                table: "ChatConversations",
                sql: "(\"AiLeaseId\" IS NULL AND \"AiLeaseExpiresAt\" IS NULL AND \"AiLeaseSourceSequence\" IS NULL) OR (\"AutomationMode\" = 'Automatic' AND \"AiLeaseId\" IS NOT NULL AND \"AiLeaseExpiresAt\" IS NOT NULL AND \"AiLeaseSourceSequence\" IS NOT NULL AND \"AiLeaseSourceSequence\" > 0 AND \"AiLeaseSourceSequence\" <= \"Version\")");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ChatConversations_AiUsage",
                table: "ChatConversations",
                sql: "\"AiProcessedSequence\" >= 0 AND \"AiProcessedSequence\" <= \"Version\" AND \"AiDailyCalls\" >= 0 AND \"AiTotalCalls\" >= 0 AND (\"AiDailyCalls\" = 0 OR \"AiCallsDate\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ChatConversations_Automation",
                table: "ChatConversations",
                sql: "\"AutomationMode\" IN ('Manual', 'Automatic') AND (\"AutomationMode\" = 'Manual' OR \"Status\" = 'Waiting')");

            migrationBuilder.CreateIndex(
                name: "IX_ChatOrderDrafts_OrderId",
                table: "ChatOrderDrafts",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatOrderDrafts_OrderRequestId",
                table: "ChatOrderDrafts",
                column: "OrderRequestId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "ChatOrderDrafts")
                       OR EXISTS (SELECT 1 FROM "ChatMessages" WHERE "Kind" = 'Assistant' OR ("Kind" = 'System' AND "ActorId" IS NULL))
                       OR EXISTS (SELECT 1 FROM "ChatConversations" WHERE "AutomationMode" = 'Automatic' OR "AiTotalCalls" > 0 OR "AiProcessedSequence" > 0) THEN
                        RAISE EXCEPTION 'AI chat data exists; preserve it before reverting this migration.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "ChatOrderDrafts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ChatMessages_Author",
                table: "ChatMessages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ChatConversations_AiLease",
                table: "ChatConversations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ChatConversations_AiUsage",
                table: "ChatConversations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ChatConversations_Automation",
                table: "ChatConversations");

            migrationBuilder.DropColumn(
                name: "AiCallsDate",
                table: "ChatConversations");

            migrationBuilder.DropColumn(
                name: "AiDailyCalls",
                table: "ChatConversations");

            migrationBuilder.DropColumn(
                name: "AiLeaseExpiresAt",
                table: "ChatConversations");

            migrationBuilder.DropColumn(
                name: "AiLeaseId",
                table: "ChatConversations");

            migrationBuilder.DropColumn(
                name: "AiLeaseSourceSequence",
                table: "ChatConversations");

            migrationBuilder.DropColumn(
                name: "AiProcessedSequence",
                table: "ChatConversations");

            migrationBuilder.DropColumn(
                name: "AiTotalCalls",
                table: "ChatConversations");

            migrationBuilder.DropColumn(
                name: "AutomationMode",
                table: "ChatConversations");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ChatMessages_Author",
                table: "ChatMessages",
                sql: "(\"Kind\" = 'Visitor' AND \"ActorId\" IS NULL AND \"ActorName\" IS NULL AND \"RequestId\" IS NOT NULL) OR (\"Kind\" = 'Staff' AND \"ActorId\" IS NOT NULL AND \"ActorName\" IS NOT NULL AND \"RequestId\" IS NOT NULL) OR (\"Kind\" = 'System' AND \"ActorId\" IS NOT NULL AND \"ActorName\" IS NOT NULL AND \"RequestId\" IS NULL)");
        }
    }
}
