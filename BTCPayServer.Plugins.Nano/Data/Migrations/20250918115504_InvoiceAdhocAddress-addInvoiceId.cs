using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Plugins.Nano.Data.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceAdhocAddressaddInvoiceId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "invoiceId",
                schema: "BTCPayServer.Plugins.Nano",
                table: "InvoiceAdhocAddress",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "invoiceId",
                schema: "BTCPayServer.Plugins.Nano",
                table: "InvoiceAdhocAddress");
        }
    }
}
