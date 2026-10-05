using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MadeInMinas.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHumanChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChatConversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    VisitorName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AssignedToId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedToName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatConversations", x => x.Id);
                    table.CheckConstraint("CK_ChatConversations_State", "(\"Status\" = 'Waiting' AND \"AssignedToId\" IS NULL AND \"AssignedToName\" IS NULL AND \"ClosedAt\" IS NULL) OR (\"Status\" = 'InService' AND \"AssignedToId\" IS NOT NULL AND \"AssignedToName\" IS NOT NULL AND \"ClosedAt\" IS NULL) OR (\"Status\" = 'Closed' AND \"AssignedToId\" IS NOT NULL AND \"AssignedToName\" IS NOT NULL AND \"ClosedAt\" IS NOT NULL)");
                    table.CheckConstraint("CK_ChatConversations_Version", "\"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_ChatConversations_Users_AssignedToId",
                        column: x => x.AssignedToId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ChatMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessages", x => x.Id);
                    table.CheckConstraint("CK_ChatMessages_Author", "(\"Kind\" = 'Visitor' AND \"ActorId\" IS NULL AND \"ActorName\" IS NULL AND \"RequestId\" IS NOT NULL) OR (\"Kind\" = 'Staff' AND \"ActorId\" IS NOT NULL AND \"ActorName\" IS NOT NULL AND \"RequestId\" IS NOT NULL) OR (\"Kind\" = 'System' AND \"ActorId\" IS NOT NULL AND \"ActorName\" IS NOT NULL AND \"RequestId\" IS NULL)");
                    table.CheckConstraint("CK_ChatMessages_Sequence", "\"Sequence\" > 0");
                    table.CheckConstraint("CK_ChatMessages_Text", "length(btrim(\"Text\")) > 0");
                    table.ForeignKey(
                        name: "FK_ChatMessages_ChatConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "ChatConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChatMessages_Users_ActorId",
                        column: x => x.ActorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatConversations_AssignedToId",
                table: "ChatConversations",
                column: "AssignedToId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatConversations_RequestId",
                table: "ChatConversations",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatConversations_Status_UpdatedAt_Id",
                table: "ChatConversations",
                columns: new[] { "Status", "UpdatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_ActorId",
                table: "ChatMessages",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_ConversationId_RequestId",
                table: "ChatMessages",
                columns: new[] { "ConversationId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_ConversationId_Sequence",
                table: "ChatMessages",
                columns: new[] { "ConversationId", "Sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "ChatConversations") THEN
                        RAISE EXCEPTION 'Chat conversations exist; archive them before reverting this migration.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "ChatMessages");

            migrationBuilder.DropTable(
                name: "ChatConversations");
        }
    }
}
