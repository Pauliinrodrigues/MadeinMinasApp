using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MadeInMinas.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIngredientStock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CurrentStock",
                table: "Ingredients",
                type: "numeric(9,3)",
                precision: 9,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<long>(
                name: "StockVersion",
                table: "Ingredients",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "StockMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IngredientId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    IngredientName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Unit = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Type = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(9,3)", precision: 9, scale: 3, nullable: false),
                    Delta = table.Column<decimal>(type: "numeric(9,3)", precision: 9, scale: 3, nullable: false),
                    PreviousBalance = table.Column<decimal>(type: "numeric(9,3)", precision: 9, scale: 3, nullable: false),
                    Balance = table.Column<decimal>(type: "numeric(9,3)", precision: 9, scale: 3, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockMovements", x => x.Id);
                    table.CheckConstraint("CK_StockMovements_Type", "(\"Type\" = 'Entry' AND \"Quantity\" > 0 AND \"Delta\" = \"Quantity\") OR (\"Type\" = 'Exit' AND \"Quantity\" > 0 AND \"Delta\" = -\"Quantity\") OR (\"Type\" = 'Count' AND \"Balance\" = \"Quantity\")");
                    table.CheckConstraint("CK_StockMovements_Unit", "\"Unit\" IN ('kg', 'l', 'un')");
                    table.CheckConstraint("CK_StockMovements_Values", "\"Quantity\" BETWEEN 0 AND 999999.999 AND \"PreviousBalance\" BETWEEN 0 AND 999999.999 AND \"Balance\" BETWEEN 0 AND 999999.999 AND \"Delta\" <> 0 AND \"Balance\" = \"PreviousBalance\" + \"Delta\" AND \"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_StockMovements_Ingredients_IngredientId",
                        column: x => x.IngredientId,
                        principalTable: "Ingredients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockMovements_Users_ActorId",
                        column: x => x.ActorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Ingredients_CurrentStock",
                table: "Ingredients",
                sql: "\"CurrentStock\" BETWEEN 0 AND 999999.999");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Ingredients_StockVersion",
                table: "Ingredients",
                sql: "\"StockVersion\" >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_ActorId",
                table: "StockMovements",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_IngredientId_RequestId",
                table: "StockMovements",
                columns: new[] { "IngredientId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_IngredientId_Version",
                table: "StockMovements",
                columns: new[] { "IngredientId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockMovements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Ingredients_CurrentStock",
                table: "Ingredients");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Ingredients_StockVersion",
                table: "Ingredients");

            migrationBuilder.DropColumn(
                name: "CurrentStock",
                table: "Ingredients");

            migrationBuilder.DropColumn(
                name: "StockVersion",
                table: "Ingredients");
        }
    }
}
