using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MadeInMinas.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_Origin",
                table: "Orders");

            migrationBuilder.AlterColumn<Guid>(
                name: "ActorId",
                table: "OrderStatusHistory",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "CreatedById",
                table: "Orders",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderStatusHistory_Actor",
                table: "OrderStatusHistory",
                sql: "\"ActorId\" IS NOT NULL OR (\"Version\" = 1 AND \"FromStatus\" IS NULL AND \"ToStatus\" = 'New')");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_RequestId",
                table: "Orders",
                column: "RequestId",
                unique: true,
                filter: "\"Origin\" = 'DirectLink'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_Origin",
                table: "Orders",
                sql: "(\"Origin\" = 'Manual' AND \"CreatedById\" IS NOT NULL) OR (\"Origin\" = 'DirectLink' AND \"CreatedById\" IS NULL AND \"Fulfillment\" = 'Pickup')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "Orders" WHERE "Origin" = 'DirectLink') THEN
                        RAISE EXCEPTION 'Public orders exist. Preserve their history before reverting AddPublicOrders.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderStatusHistory_Actor",
                table: "OrderStatusHistory");

            migrationBuilder.DropIndex(
                name: "IX_Orders_RequestId",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_Origin",
                table: "Orders");

            migrationBuilder.AlterColumn<Guid>(
                name: "ActorId",
                table: "OrderStatusHistory",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "CreatedById",
                table: "Orders",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_Origin",
                table: "Orders",
                sql: "\"Origin\" = 'Manual'");
        }
    }
}
