namespace BTCPayServer.Plugins.Nano.Data.DTO;

public class InvoiceAdhocAddressDto
{
    public string id { get; set; }
    public string publicAddress { get; set; }
    public string privateAddress { get; set; }
    public string account { get; set; }
    public string invoiceId { get; set; }
}