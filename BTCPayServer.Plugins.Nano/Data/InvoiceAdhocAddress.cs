using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace BTCPayServer.Plugins.Nano.Data;

public class InvoiceAdhocAddress
{
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public string id { get; set; }

    public string publicAddress { get; set; }

    public string privateAddress { get; set; }

    public string account { get; set; }

    // TODO: Add foreign key
    public string invoiceId { get; set; }
}
