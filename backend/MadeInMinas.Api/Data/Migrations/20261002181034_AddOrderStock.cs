using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MadeInMinas.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderStock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OrderId",
                table: "StockMovements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StockStatus",
                table: "Orders",
                type: "character varying(12)",
                maxLength: 12,
                nullable: false,
                defaultValue: "Pending");

            // Pedidos já processados não recebem consumo ou devolução retroativos.
            migrationBuilder.Sql("UPDATE \"Orders\" SET \"StockStatus\" = 'Legacy' WHERE \"Status\" <> 'New'");

            migrationBuilder.CreateTable(
                name: "OrderStockComponents",
                columns: table => new
                {
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    IngredientId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    IngredientName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Unit = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    ProductQuantity = table.Column<int>(type: "integer", nullable: false),
                    RecipeYield = table.Column<int>(type: "integer", nullable: false),
                    RecipeQuantity = table.Column<decimal>(type: "numeric(9,3)", precision: 9, scale: 3, nullable: false),
                    ConsumedQuantity = table.Column<decimal>(type: "numeric(9,3)", precision: 9, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderStockComponents", x => new { x.OrderId, x.ProductId, x.IngredientId });
                    table.CheckConstraint("CK_OrderStockComponents_Quantities", "\"ProductQuantity\" BETWEEN 1 AND 99 AND \"RecipeYield\" BETWEEN 1 AND 10000 AND \"RecipeQuantity\" BETWEEN 0.001 AND 999999.999 AND \"ConsumedQuantity\" BETWEEN 0.001 AND 999999.999");
                    table.CheckConstraint("CK_OrderStockComponents_Unit", "\"Unit\" IN ('kg', 'l', 'un')");
                    table.ForeignKey(
                        name: "FK_OrderStockComponents_Ingredients_IngredientId",
                        column: x => x.IngredientId,
                        principalTable: "Ingredients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderStockComponents_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrderStockComponents_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_OrderId_IngredientId_Type",
                table: "StockMovements",
                columns: new[] { "OrderId", "IngredientId", "Type" },
                unique: true,
                filter: "\"OrderId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockMovements_OrderType",
                table: "StockMovements",
                sql: "\"OrderId\" IS NULL OR \"Type\" IN ('Entry','Exit')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_StockStatus",
                table: "Orders",
                sql: "\"StockStatus\" IN ('Pending','Consumed','Returned','Retained','Legacy','NotRequired')");

            migrationBuilder.CreateIndex(
                name: "IX_OrderStockComponents_IngredientId",
                table: "OrderStockComponents",
                column: "IngredientId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderStockComponents_ProductId",
                table: "OrderStockComponents",
                column: "ProductId");

            migrationBuilder.AddForeignKey(
                name: "FK_StockMovements_Orders_OrderId",
                table: "StockMovements",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StockMovements_Orders_OrderId",
                table: "StockMovements");

            migrationBuilder.DropTable(
                name: "OrderStockComponents");

            migrationBuilder.DropIndex(
                name: "IX_StockMovements_OrderId_IngredientId_Type",
                table: "StockMovements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StockMovements_OrderType",
                table: "StockMovements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_StockStatus",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "OrderId",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "StockStatus",
                table: "Orders");
        }
    }
}
