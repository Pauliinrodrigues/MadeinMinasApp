using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MadeInMinas.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddManualPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    Method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    CashTendered = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.CheckConstraint("CK_Payments_Amount", "\"Amount\" > 0");
                    table.CheckConstraint("CK_Payments_Cash", "(\"Method\" = 'Cash' AND \"Status\" IN ('Received','Refunded') AND \"CashTendered\" IS NOT NULL AND \"CashTendered\" >= \"Amount\") OR ((\"Method\" <> 'Cash' OR \"Status\" IN ('Pending','Cancelled')) AND \"CashTendered\" IS NULL)");
                    table.CheckConstraint("CK_Payments_Method", "\"Method\" IN ('Cash','Pix','CreditCard','DebitCard')");
                    table.CheckConstraint("CK_Payments_Status", "\"Status\" IN ('Pending','Received','Cancelled','Refunded') AND \"Version\" >= 1");
                    table.ForeignKey(
                        name: "FK_Payments_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_Users_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentStatusHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ToStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentStatusHistory", x => x.Id);
                    table.CheckConstraint("CK_PaymentStatusHistory_Reason", "\"ToStatus\" NOT IN ('Cancelled','Refunded') OR (\"Reason\" IS NOT NULL AND length(btrim(\"Reason\")) > 0)");
                    table.CheckConstraint("CK_PaymentStatusHistory_Transition", "(\"Version\" = 1 AND \"FromStatus\" IS NULL AND \"ToStatus\" = 'Pending') OR (\"Version\" > 1 AND \"FromStatus\" IS NOT NULL AND ((\"FromStatus\" = 'Pending' AND \"ToStatus\" IN ('Received','Cancelled')) OR (\"FromStatus\" = 'Received' AND \"ToStatus\" = 'Refunded')))");
                    table.ForeignKey(
                        name: "FK_PaymentStatusHistory_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PaymentStatusHistory_Users_ActorId",
                        column: x => x.ActorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ActiveOrder",
                table: "Payments",
                column: "OrderId",
                unique: true,
                filter: "\"Status\" IN ('Pending','Received')");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_CreatedById_RequestId",
                table: "Payments",
                columns: new[] { "CreatedById", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_OrderId_CreatedAt_Id",
                table: "Payments",
                columns: new[] { "OrderId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentStatusHistory_ActorId",
                table: "PaymentStatusHistory",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentStatusHistory_PaymentId_Version",
                table: "PaymentStatusHistory",
                columns: new[] { "PaymentId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentStatusHistory");

            migrationBuilder.DropTable(
                name: "Payments");
        }
    }
}
