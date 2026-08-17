using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace B2B.Api.Migrations
{
    /// <inheritdoc />
    public partial class PortalClientIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClientExternalId",
                table: "Users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClientNumber",
                table: "Users",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Culture",
                table: "Users",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                // El usuario de integración existente conserva la cultura por defecto del portal
                defaultValue: "es_ES");

            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "Users",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                // Los usuarios ya existentes son el usuario técnico del conector
                defaultValue: "integration");

            migrationBuilder.CreateIndex(
                name: "IX_Users_ClientExternalId",
                table: "Users",
                column: "ClientExternalId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_ClientExternalId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ClientExternalId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ClientNumber",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Culture",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "Users");
        }
    }
}
