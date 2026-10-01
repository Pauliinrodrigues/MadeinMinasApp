using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MadeInMinas.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddKitchenStatuses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderStatusHistory_Transition",
                table: "OrderStatusHistory");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_Status",
                table: "Orders");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderStatusHistory_Transition",
                table: "OrderStatusHistory",
                sql: "(\"Version\" = 1 AND \"FromStatus\" IS NULL AND \"ToStatus\" = 'New') OR (\"Version\" > 1 AND \"FromStatus\" IS NOT NULL AND ((\"FromStatus\" = 'New' AND \"ToStatus\" IN ('Confirmed','Cancelled')) OR (\"FromStatus\" = 'Confirmed' AND \"ToStatus\" IN ('InPreparation','Cancelled')) OR (\"FromStatus\" = 'InPreparation' AND \"ToStatus\" IN ('Ready','Cancelled')) OR (\"FromStatus\" = 'Ready' AND \"ToStatus\" = 'Cancelled')))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_Status",
                table: "Orders",
                sql: "\"Status\" IN ('New','Confirmed','InPreparation','Ready','Cancelled') AND \"Version\" >= 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderStatusHistory_Transition",
                table: "OrderStatusHistory");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_Status",
                table: "Orders");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderStatusHistory_Transition",
                table: "OrderStatusHistory",
                sql: "(\"Version\" = 1 AND \"FromStatus\" IS NULL AND \"ToStatus\" = 'New') OR (\"Version\" > 1 AND \"FromStatus\" IS NOT NULL AND ((\"FromStatus\" = 'New' AND \"ToStatus\" IN ('Confirmed','Cancelled')) OR (\"FromStatus\" = 'Confirmed' AND \"ToStatus\" = 'Cancelled')))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_Status",
                table: "Orders",
                sql: "\"Status\" IN ('New','Confirmed','Cancelled') AND \"Version\" >= 1");
        }
    }
}
