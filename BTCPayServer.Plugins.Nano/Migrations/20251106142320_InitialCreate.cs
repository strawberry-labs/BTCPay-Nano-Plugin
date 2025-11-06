using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Plugins.Nano.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "BTCPayServer.Plugins.Nano");

            migrationBuilder.CreateTable(
                name: "InvoiceAdhocAddress",
                schema: "BTCPayServer.Plugins.Nano",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    publicAddress = table.Column<string>(type: "text", nullable: true),
                    privateAddress = table.Column<string>(type: "text", nullable: true),
                    account = table.Column<string>(type: "text", nullable: true),
                    invoiceId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceAdhocAddress", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoiceAdhocAddress",
                schema: "BTCPayServer.Plugins.Nano");
        }
    }
}
