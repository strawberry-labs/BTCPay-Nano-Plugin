namespace BTCPayServer.Plugins.Nano.Payments
{
    public class NanoLikePaymentData
    {
        public string AdhocAccount { get; set; }
        public string SendHash { get; set; }
        public string ReceiveHash { get; set; }
        public string AmountRaw { get; set; }
        public bool Confirmation { get; set; }
    }
}