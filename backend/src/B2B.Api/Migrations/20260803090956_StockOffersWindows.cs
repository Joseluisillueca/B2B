using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace B2B.Api.Migrations
{
    /// <inheritdoc />
    public partial class StockOffersWindows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Offers",
                columns: table => new
                {
                    ExternalId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ModelId = table.Column<string>(type: "text", nullable: false),
                    ProductId = table.Column<string>(type: "text", nullable: true),
                    ClientId = table.Column<string>(type: "text", nullable: true),
                    ClientGroupId = table.Column<string>(type: "text", nullable: true),
                    PriceType = table.Column<string>(type: "text", nullable: false),
                    PriceCode = table.Column<string>(type: "text", nullable: false),
                    PriceValue = table.Column<decimal>(type: "numeric", nullable: false),
                    MinQuantity = table.Column<decimal>(type: "numeric", nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "numeric", nullable: true),
                    FromDate = table.Column<string>(type: "text", nullable: true),
                    ToDate = table.Column<string>(type: "text", nullable: true),
                    OrderType = table.Column<string>(type: "text", nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Offers", x => x.ExternalId);
                });

            migrationBuilder.CreateTable(
                name: "ServiceWindows",
                columns: table => new
                {
                    ExternalId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    OrderType = table.Column<string>(type: "text", nullable: false),
                    FromDate = table.Column<string>(type: "text", nullable: false),
                    ToDate = table.Column<string>(type: "text", nullable: false),
                    LimitDate = table.Column<string>(type: "text", nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceWindows", x => x.ExternalId);
                });

            migrationBuilder.CreateTable(
                name: "StockLevels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductExternalId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ServiceWindowId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ServiceWindowKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Stock = table.Column<decimal>(type: "numeric", nullable: false),
                    OrderType = table.Column<string>(type: "text", nullable: false),
                    EntryDate = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockLevels", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Offers_ModelId",
                table: "Offers",
                column: "ModelId");

            migrationBuilder.CreateIndex(
                name: "IX_Offers_ProductId",
                table: "Offers",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_StockLevels_ProductExternalId_ServiceWindowKey",
                table: "StockLevels",
                columns: new[] { "ProductExternalId", "ServiceWindowKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Offers");

            migrationBuilder.DropTable(
                name: "ServiceWindows");

            migrationBuilder.DropTable(
                name: "StockLevels");
        }
    }
}
