using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace B2B.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BodyHtml",
                table: "notification_channels",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Subject",
                table: "notification_channels",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailLayoutHtml",
                table: "integration_settings",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BodyHtml",
                table: "notification_channels");

            migrationBuilder.DropColumn(
                name: "Subject",
                table: "notification_channels");

            migrationBuilder.DropColumn(
                name: "EmailLayoutHtml",
                table: "integration_settings");
        }
    }
}
