using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace B2B.Api.Migrations
{
    /// <inheritdoc />
    public partial class PortalAccountAndReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "Users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "business_change_requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Section = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ChangesJson = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_change_requests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "contact_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Message = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    AttachmentName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    AttachmentPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DeliveredTo = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contact_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "portal_user_prefs",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShowPrices = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ListDesktop = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ListMobile = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ShippingAddressId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_portal_user_prefs", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "return_requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ClientId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PickupSlot = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Packages = table.Column<int>(type: "integer", nullable: false),
                    Items = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Resolution = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    PhotoUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Reference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_return_requests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_business_change_requests_ClientId_Status",
                table: "business_change_requests",
                columns: new[] { "ClientId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_contact_messages_ClientId",
                table: "contact_messages",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_return_requests_ClientId_Status",
                table: "return_requests",
                columns: new[] { "ClientId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_return_requests_Code",
                table: "return_requests",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "business_change_requests");

            migrationBuilder.DropTable(
                name: "contact_messages");

            migrationBuilder.DropTable(
                name: "portal_user_prefs");

            migrationBuilder.DropTable(
                name: "return_requests");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "Users");
        }
    }
}
