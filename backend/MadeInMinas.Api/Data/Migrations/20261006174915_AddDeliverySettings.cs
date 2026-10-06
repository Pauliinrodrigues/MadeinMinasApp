using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MadeInMinas.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliverySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeliverySettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    AreasJson = table.Column<string>(type: "jsonb", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedByName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliverySettings", x => x.Id);
                    table.CheckConstraint("CK_DeliverySettings_Areas", "jsonb_typeof(\"AreasJson\") = 'array' AND jsonb_array_length(\"AreasJson\") <= 500");
                    table.CheckConstraint("CK_DeliverySettings_Singleton", "\"Id\" = 1 AND \"Version\" > 0");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "DeliverySettings") THEN
                        RAISE EXCEPTION 'Delivery settings exist. Export and migrate the saved coverage before reverting AddDeliverySettings.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "DeliverySettings");
        }
    }
}
