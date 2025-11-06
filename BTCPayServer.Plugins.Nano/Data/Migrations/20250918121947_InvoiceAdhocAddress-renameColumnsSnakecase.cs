using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Plugins.Nano.Data.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceAdhocAddressrenameColumnsSnakecase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PublicAddress",
                schema: "BTCPayServer.Plugins.Nano",
                table: "InvoiceAdhocAddress",
                newName: "publicAddress");

            migrationBuilder.RenameColumn(
                name: "PrivateAddress",
                schema: "BTCPayServer.Plugins.Nano",
                table: "InvoiceAdhocAddress",
                newName: "privateAddress");

            migrationBuilder.RenameColumn(
                name: "Account",
                schema: "BTCPayServer.Plugins.Nano",
                table: "InvoiceAdhocAddress",
                newName: "account");

            migrationBuilder.RenameColumn(
                name: "Id",
                schema: "BTCPayServer.Plugins.Nano",
                table: "InvoiceAdhocAddress",
                newName: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "publicAddress",
                schema: "BTCPayServer.Plugins.Nano",
                table: "InvoiceAdhocAddress",
                newName: "PublicAddress");

            migrationBuilder.RenameColumn(
                name: "privateAddress",
                schema: "BTCPayServer.Plugins.Nano",
                table: "InvoiceAdhocAddress",
                newName: "PrivateAddress");

            migrationBuilder.RenameColumn(
                name: "account",
                schema: "BTCPayServer.Plugins.Nano",
                table: "InvoiceAdhocAddress",
                newName: "Account");

            migrationBuilder.RenameColumn(
                name: "id",
                schema: "BTCPayServer.Plugins.Nano",
                table: "InvoiceAdhocAddress",
                newName: "Id");
        }
    }
}
