using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Plugins.Nano.Data.Migrations
{
    /// <inheritdoc />
    public partial class Test1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_TestData",
                schema: "BTCPayServer.Plugins.Nano",
                table: "TestData");

            migrationBuilder.RenameTable(
                name: "TestData",
                schema: "BTCPayServer.Plugins.Nano",
                newName: "TableTest",
                newSchema: "BTCPayServer.Plugins.Nano");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TableTest",
                schema: "BTCPayServer.Plugins.Nano",
                table: "TableTest",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "TableTest2",
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
                    table.PrimaryKey("PK_TableTest2", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TableTest2",
                schema: "BTCPayServer.Plugins.Nano");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TableTest",
                schema: "BTCPayServer.Plugins.Nano",
                table: "TableTest");

            migrationBuilder.RenameTable(
                name: "TableTest",
                schema: "BTCPayServer.Plugins.Nano",
                newName: "TestData",
                newSchema: "BTCPayServer.Plugins.Nano");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TestData",
                schema: "BTCPayServer.Plugins.Nano",
                table: "TestData",
                column: "Id");
        }
    }
}
