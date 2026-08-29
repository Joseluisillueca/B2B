using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace B2B.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBcIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceJson",
                table: "carts",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "document_sources",
                columns: table => new
                {
                    DocType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SourceType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Endpoint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Transformer = table.Column<string>(type: "text", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_sources", x => x.DocType);
                });

            migrationBuilder.CreateTable(
                name: "integration_settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BcBaseUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    BcTokenUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    BcClientId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    BcClientSecret = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    BcScope = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ApiRestBaseUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ApiRestHeadersJson = table.Column<string>(type: "jsonb", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "notification_channels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ChannelType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    Fixed = table.Column<bool>(type: "boolean", nullable: false),
                    Endpoint = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Transformer = table.Column<string>(type: "text", nullable: true),
                    ToVars = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CcVars = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    BccVars = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_channels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "notification_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ChannelType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Detail = table.Column<string>(type: "text", nullable: true),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_logs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_notification_channels_EventKey",
                table: "notification_channels",
                column: "EventKey");

            migrationBuilder.CreateIndex(
                name: "IX_notification_logs_CreatedAt",
                table: "notification_logs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_notification_logs_EventKey",
                table: "notification_logs",
                column: "EventKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_sources");

            migrationBuilder.DropTable(
                name: "integration_settings");

            migrationBuilder.DropTable(
                name: "notification_channels");

            migrationBuilder.DropTable(
                name: "notification_logs");

            migrationBuilder.DropColumn(
                name: "SourceJson",
                table: "carts");
        }
    }
}
