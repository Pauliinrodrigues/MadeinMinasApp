using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MadeInMinas.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPrintQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PrintJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderNumber = table.Column<long>(type: "bigint", nullable: false),
                    Mode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    RequestKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    ClaimId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClaimedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrintJobs", x => x.Id);
                    table.CheckConstraint("CK_PrintJobs_State", "\"State\" IN ('Queued', 'Claimed', 'Submitted', 'Review', 'Cancelled')");
                    table.ForeignKey(
                        name: "FK_PrintJobs_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PrintStations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    KeyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Automatic = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrintStations", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "PrintStations",
                columns: new[] { "Id", "Automatic", "KeyHash", "LastSeenAt", "Version" },
                values: new object[] { 1, false, null, null, 1 });

            migrationBuilder.CreateIndex(
                name: "IX_PrintJobs_OrderId",
                table: "PrintJobs",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PrintJobs_RequestKey",
                table: "PrintJobs",
                column: "RequestKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrintJobs_State_CreatedAt",
                table: "PrintJobs",
                columns: new[] { "State", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "PrintJobs") OR EXISTS (SELECT 1 FROM "PrintStations" WHERE "KeyHash" IS NOT NULL) THEN
                        RAISE EXCEPTION 'A fila de impressão contém histórico ou estação cadastrada. Planeje uma reversão que preserve estes dados.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "PrintJobs");

            migrationBuilder.DropTable(
                name: "PrintStations");
        }
    }
}
