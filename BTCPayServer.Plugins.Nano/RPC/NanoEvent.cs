// Event model your confirmations service should publish
// Adjust/relocate if you already defined this elsewhere

namespace BTCPayServer.Plugins.Nano.RPC
{
    public enum NanoEventKind
    {
        SendToAdhocConfirmed, // confirmed send targeting the invoice's adhoc account
        ReceiveOnAdhocConfirmed, // confirmed receive on the adhoc account (after we created it)
        SendToStoreWalletConfirmed, // confirmed send to store wallet (outbound sweep)
        ReceiveOnStoreWalletConfirmed // confirmed receive on store wallet
    }
    public class NanoEvent
    {
        public string CryptoCode { get; set; }
        public NanoEventKind Kind { get; set; }

        // Common
        public string Account { get; set; }        // account the block applies to (adhoc or store wallet)
        public string BlockHash { get; set; }      // confirmed block hash
        public string AmountRaw { get; set; }        // amount in raw
        public bool Confirmation { get; set; } // optional (confirmation topic is finality, but included for parity)

        // Send-specific
        public string FromAccount { get; set; }    // sender account (for Send events)
        public string ToAccount { get; set; }      // destination account (for Send events), typically the adhoc or store wallet

        // Linkage
        public string SourceSendHash { get; set; } // for receive events, the originating send hash if available
        public string StoreId { get; set; }
    }
}