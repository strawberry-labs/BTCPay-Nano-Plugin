namespace BTCPayServer.Plugins.Nano.Payments
{
    public class NanoPaymentPromptDetails
    {
        public long AccountIndex { get; set; }
        public long? InvoiceSettledConfirmationThreshold { get; set; }
    }
}