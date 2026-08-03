using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace B2B.Api.Migrations
{
    /// <inheritdoc />
    public partial class CatalogTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CatalogModels",
                columns: table => new
                {
                    ExternalId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    ExternalReference = table.Column<string>(type: "text", nullable: false),
                    FamilyId = table.Column<string>(type: "text", nullable: false),
                    NameTranslationsJson = table.Column<string>(type: "jsonb", nullable: false),
                    AttributesJson = table.Column<string>(type: "jsonb", nullable: false),
                    ProductSegmentsJson = table.Column<string>(type: "jsonb", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogModels", x => x.ExternalId);
                });

            migrationBuilder.CreateTable(
                name: "CatalogProducts",
                columns: table => new
                {
                    ExternalId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ModelExternalId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    Sku = table.Column<string>(type: "text", nullable: false),
                    Ean = table.Column<string>(type: "text", nullable: false),
                    Size = table.Column<string>(type: "text", nullable: true),
                    TaxId = table.Column<string>(type: "text", nullable: false),
                    AttributesJson = table.Column<string>(type: "jsonb", nullable: false),
                    IsCasePack = table.Column<bool>(type: "boolean", nullable: false),
                    BundleJson = table.Column<string>(type: "jsonb", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogProducts", x => x.ExternalId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogModels_FamilyId",
                table: "CatalogModels",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogProducts_ModelExternalId",
                table: "CatalogProducts",
                column: "ModelExternalId");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogProducts_Sku",
                table: "CatalogProducts",
                column: "Sku");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CatalogModels");

            migrationBuilder.DropTable(
                name: "CatalogProducts");
        }
    }
}
