using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace B2B.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sales_rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    ConditionsJson = table.Column<string>(type: "jsonb", nullable: false),
                    ActionsJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_rules", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sales_rules_Priority",
                table: "sales_rules",
                column: "Priority");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sales_rules");
        }
    }
}
