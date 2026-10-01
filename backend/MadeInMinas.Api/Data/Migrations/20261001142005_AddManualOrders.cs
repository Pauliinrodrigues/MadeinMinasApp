using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MadeInMinas.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddManualOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CustomerPhone = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    Fulfillment = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    AddressId = table.Column<Guid>(type: "uuid", nullable: true),
                    AddressStreet = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    AddressNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    AddressNeighborhood = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    AddressCity = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    AddressState = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    AddressComplement = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    AddressPostalCode = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    AddressReference = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    Origin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Subtotal = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    DeliveryFee = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                    table.CheckConstraint("CK_Orders_Amounts", "\"Subtotal\" > 0 AND \"DeliveryFee\" BETWEEN 0 AND 9999.99 AND \"Total\" = \"Subtotal\" + \"DeliveryFee\"");
                    table.CheckConstraint("CK_Orders_Fulfillment", "(\"Fulfillment\" = 'Pickup' AND \"AddressId\" IS NULL AND \"AddressStreet\" IS NULL AND \"AddressNumber\" IS NULL AND \"AddressNeighborhood\" IS NULL AND \"AddressCity\" IS NULL AND \"AddressState\" IS NULL AND \"AddressComplement\" IS NULL AND \"AddressPostalCode\" IS NULL AND \"AddressReference\" IS NULL AND \"DeliveryFee\" = 0) OR (\"Fulfillment\" = 'Delivery' AND \"AddressId\" IS NOT NULL AND \"AddressStreet\" IS NOT NULL AND \"AddressNumber\" IS NOT NULL AND \"AddressNeighborhood\" IS NOT NULL AND \"AddressCity\" IS NOT NULL AND \"AddressState\" IS NOT NULL)");
                    table.CheckConstraint("CK_Orders_Number", "\"Number\" > 0");
                    table.CheckConstraint("CK_Orders_Origin", "\"Origin\" = 'Manual'");
                    table.CheckConstraint("CK_Orders_Status", "\"Status\" IN ('New','Confirmed','Cancelled') AND \"Version\" >= 1");
                    table.ForeignKey(
                        name: "FK_Orders_Addresses_AddressId",
                        column: x => x.AddressId,
                        principalTable: "Addresses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Orders_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Orders_Users_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    LineTotal = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Notes = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderItems", x => x.Id);
                    table.CheckConstraint("CK_OrderItems_Amounts", "\"UnitPrice\" > 0 AND \"LineTotal\" = \"UnitPrice\" * \"Quantity\"");
                    table.CheckConstraint("CK_OrderItems_Position", "\"Position\" BETWEEN 1 AND 50");
                    table.CheckConstraint("CK_OrderItems_Quantity", "\"Quantity\" BETWEEN 1 AND 99");
                    table.ForeignKey(
                        name: "FK_OrderItems_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrderItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderStatusHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_OrderStatusHistory", x => x.Id);
                    table.CheckConstraint("CK_OrderStatusHistory_Reason", "\"ToStatus\" <> 'Cancelled' OR (\"Reason\" IS NOT NULL AND length(btrim(\"Reason\")) > 0)");
                    table.CheckConstraint("CK_OrderStatusHistory_Transition", "(\"Version\" = 1 AND \"FromStatus\" IS NULL AND \"ToStatus\" = 'New') OR (\"Version\" > 1 AND \"FromStatus\" IS NOT NULL AND ((\"FromStatus\" = 'New' AND \"ToStatus\" IN ('Confirmed','Cancelled')) OR (\"FromStatus\" = 'Confirmed' AND \"ToStatus\" = 'Cancelled')))");
                    table.ForeignKey(
                        name: "FK_OrderStatusHistory_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrderStatusHistory_Users_ActorId",
                        column: x => x.ActorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_OrderId_Position",
                table: "OrderItems",
                columns: new[] { "OrderId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_ProductId",
                table: "OrderItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_AddressId",
                table: "Orders",
                column: "AddressId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CreatedAt_Number",
                table: "Orders",
                columns: new[] { "CreatedAt", "Number" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CreatedById_RequestId",
                table: "Orders",
                columns: new[] { "CreatedById", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CustomerId_CreatedAt_Number",
                table: "Orders",
                columns: new[] { "CustomerId", "CreatedAt", "Number" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Number",
                table: "Orders",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Status_CreatedAt_Number",
                table: "Orders",
                columns: new[] { "Status", "CreatedAt", "Number" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderStatusHistory_ActorId",
                table: "OrderStatusHistory",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderStatusHistory_OrderId_Version",
                table: "OrderStatusHistory",
                columns: new[] { "OrderId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderItems");

            migrationBuilder.DropTable(
                name: "OrderStatusHistory");

            migrationBuilder.DropTable(
                name: "Orders");
        }
    }
}
