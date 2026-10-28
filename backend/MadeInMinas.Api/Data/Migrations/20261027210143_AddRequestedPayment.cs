using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MadeInMinas.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRequestedPayment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ChangeFor",
                table: "Orders",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentPreference",
                table: "Orders",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ChangeFor",
                table: "ChatOrderDrafts",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentPreference",
                table: "ChatOrderDrafts",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_RequestedPayment",
                table: "Orders",
                sql: "(\"PaymentPreference\" IS NULL OR \"PaymentPreference\" IN ('Card','Cash','Pix')) AND (\"ChangeFor\" IS NULL OR (\"PaymentPreference\" IS NOT NULL AND \"PaymentPreference\" = 'Cash' AND \"ChangeFor\" >= \"Total\" AND \"ChangeFor\" <= 999999.99))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ChatOrderDrafts_RequestedPayment",
                table: "ChatOrderDrafts",
                sql: "(\"PaymentPreference\" IS NULL OR \"PaymentPreference\" IN ('Card','Cash','Pix')) AND (\"ChangeFor\" IS NULL OR (\"PaymentPreference\" IS NOT NULL AND \"PaymentPreference\" = 'Cash' AND \"ChangeFor\" > 0 AND \"ChangeFor\" <= 999999.99))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM "Orders" WHERE "PaymentPreference" IS NOT NULL OR "ChangeFor" IS NOT NULL)
                    OR EXISTS (SELECT 1 FROM "ChatOrderDrafts" WHERE "PaymentPreference" IS NOT NULL OR "ChangeFor" IS NOT NULL) THEN
                    RAISE EXCEPTION 'Payment preferences exist; preserve the schema and use a compatible application rollback.';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_RequestedPayment",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ChatOrderDrafts_RequestedPayment",
                table: "ChatOrderDrafts");

            migrationBuilder.DropColumn(
                name: "ChangeFor",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentPreference",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ChangeFor",
                table: "ChatOrderDrafts");

            migrationBuilder.DropColumn(
                name: "PaymentPreference",
                table: "ChatOrderDrafts");
        }
    }
}
