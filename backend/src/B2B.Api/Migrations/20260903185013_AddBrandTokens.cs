using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace B2B.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBrandTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BrandTokensJson",
                table: "integration_settings",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BrandTokensJson",
                table: "integration_settings");
        }
    }
}
