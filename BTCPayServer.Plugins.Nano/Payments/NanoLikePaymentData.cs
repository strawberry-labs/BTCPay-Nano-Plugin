namespace BTCPayServer.Plugins.Nano.Payments
{
    public class NanoLikePaymentData
    {
        public long SubaddressIndex { get; set; }
        public long SubaccountIndex { get; set; }
        public long BlockHeight { get; set; }
        public long ConfirmationCount { get; set; }
        public string TransactionId { get; set; }
        public long? InvoiceSettledConfirmationThreshold { get; set; }

        public long LockTime { get; set; } = 0;
    }
}