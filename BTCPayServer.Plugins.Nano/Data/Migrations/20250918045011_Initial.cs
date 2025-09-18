using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Plugins.Nano.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "BTCPayServer.Plugins.Nano");

            migrationBuilder.CreateTable(
                name: "PluginRecords",
                schema: "BTCPayServer.Plugins.Nano",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PluginRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TableTest",
                schema: "BTCPayServer.Plugins.Nano",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Timestamp123 = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    test123 = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TableTest", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PluginRecords",
                schema: "BTCPayServer.Plugins.Nano");

            migrationBuilder.DropTable(
                name: "TableTest",
                schema: "BTCPayServer.Plugins.Nano");
        }
    }
}
