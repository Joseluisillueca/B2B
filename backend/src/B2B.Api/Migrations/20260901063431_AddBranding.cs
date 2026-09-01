using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace B2B.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBranding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BrandColor",
                table: "integration_settings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BrandLogoUrl",
                table: "integration_settings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BrandName",
                table: "integration_settings",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BrandColor",
                table: "integration_settings");

            migrationBuilder.DropColumn(
                name: "BrandLogoUrl",
                table: "integration_settings");

            migrationBuilder.DropColumn(
                name: "BrandName",
                table: "integration_settings");
        }
    }
}
