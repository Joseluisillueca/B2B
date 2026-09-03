using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace B2B.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "catalog_visibility",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SubjectId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    RulesJson = table.Column<string>(type: "jsonb", nullable: false),
                    Source = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalog_visibility", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_catalog_visibility_SubjectType_SubjectId_Source",
                table: "catalog_visibility",
                columns: new[] { "SubjectType", "SubjectId", "Source" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "catalog_visibility");
        }
    }
}
