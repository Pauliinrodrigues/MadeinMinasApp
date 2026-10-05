using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MadeInMinas.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_Fulfillment",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_Origin",
                table: "Orders");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_Fulfillment",
                table: "Orders",
                sql: "(\"Fulfillment\" = 'Pickup' AND \"AddressId\" IS NULL AND \"AddressStreet\" IS NULL AND \"AddressNumber\" IS NULL AND \"AddressNeighborhood\" IS NULL AND \"AddressCity\" IS NULL AND \"AddressState\" IS NULL AND \"AddressComplement\" IS NULL AND \"AddressPostalCode\" IS NULL AND \"AddressReference\" IS NULL AND \"DeliveryFee\" = 0) OR (\"Fulfillment\" = 'Delivery' AND ((\"Origin\" = 'Manual' AND \"AddressId\" IS NOT NULL) OR (\"Origin\" = 'DirectLink' AND \"AddressId\" IS NULL)) AND \"AddressStreet\" IS NOT NULL AND \"AddressNumber\" IS NOT NULL AND \"AddressNeighborhood\" IS NOT NULL AND \"AddressCity\" IS NOT NULL AND \"AddressState\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_Origin",
                table: "Orders",
                sql: "(\"Origin\" = 'Manual' AND \"CreatedById\" IS NOT NULL) OR (\"Origin\" = 'DirectLink' AND \"CreatedById\" IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "Orders" WHERE "Origin" = 'DirectLink' AND "Fulfillment" = 'Delivery') THEN
                        RAISE EXCEPTION 'Public deliveries exist. Preserve their addresses before reverting AddPublicDelivery.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_Fulfillment",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_Origin",
                table: "Orders");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_Fulfillment",
                table: "Orders",
                sql: "(\"Fulfillment\" = 'Pickup' AND \"AddressId\" IS NULL AND \"AddressStreet\" IS NULL AND \"AddressNumber\" IS NULL AND \"AddressNeighborhood\" IS NULL AND \"AddressCity\" IS NULL AND \"AddressState\" IS NULL AND \"AddressComplement\" IS NULL AND \"AddressPostalCode\" IS NULL AND \"AddressReference\" IS NULL AND \"DeliveryFee\" = 0) OR (\"Fulfillment\" = 'Delivery' AND \"AddressId\" IS NOT NULL AND \"AddressStreet\" IS NOT NULL AND \"AddressNumber\" IS NOT NULL AND \"AddressNeighborhood\" IS NOT NULL AND \"AddressCity\" IS NOT NULL AND \"AddressState\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_Origin",
                table: "Orders",
                sql: "(\"Origin\" = 'Manual' AND \"CreatedById\" IS NOT NULL) OR (\"Origin\" = 'DirectLink' AND \"CreatedById\" IS NULL AND \"Fulfillment\" = 'Pickup')");
        }
    }
}
