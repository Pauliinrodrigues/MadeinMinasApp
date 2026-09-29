using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MadeInMinas.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIngredients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Ingredients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Unit = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    MinimumStock = table.Column<decimal>(type: "numeric(9,3)", precision: 9, scale: 3, nullable: false),
                    Supplier = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ingredients", x => x.Id);
                    table.CheckConstraint("CK_Ingredients_MinimumStock", "\"MinimumStock\" BETWEEN 0 AND 999999.999");
                    table.CheckConstraint("CK_Ingredients_Unit", "\"Unit\" IN ('kg', 'l', 'un')");
                    table.CheckConstraint("CK_Ingredients_UnitCost", "\"UnitCost\" BETWEEN 0 AND 999999.9999");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Ingredients_NormalizedName",
                table: "Ingredients",
                column: "NormalizedName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Ingredients");
        }
    }
}
