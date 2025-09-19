namespace BTCPayServer.Plugins.Nano.Payments
{
    public class NanoLikeOnChainPaymentMethodDetails
    {
        // public long AccountIndex { get; set; }
        // public long AddressIndex { get; set; }
        public string AddressId { get; set; }
        public bool? InvoiceSettledConfirmation { get; set; }
    }
}