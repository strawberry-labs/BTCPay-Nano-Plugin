using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Plugins.Nano.Data.Migrations
{
    /// <inheritdoc />
    public partial class Test : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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
        }
    }
}
