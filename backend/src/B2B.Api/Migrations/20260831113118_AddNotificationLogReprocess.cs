using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace B2B.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationLogReprocess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Endpoint",
                table: "notification_logs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InputJson",
                table: "notification_logs",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Endpoint",
                table: "notification_logs");

            migrationBuilder.DropColumn(
                name: "InputJson",
                table: "notification_logs");
        }
    }
}
