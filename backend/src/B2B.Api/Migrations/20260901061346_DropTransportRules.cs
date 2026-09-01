using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace B2B.Api.Migrations
{
    /// <inheritdoc />
    public partial class DropTransportRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "transport_rules");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "transport_rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    ClientExternalId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Cost = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CountryIsoId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IncotermId = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    MinAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    MinUnits = table.Column<int>(type: "integer", nullable: true),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    OrderType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    PerUnit = table.Column<bool>(type: "boolean", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transport_rules", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_transport_rules_Priority",
                table: "transport_rules",
                column: "Priority");
        }
    }
}
