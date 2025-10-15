using System;
using System.Collections.Generic;
using BTCPayServer.Models;

namespace BTCPayServer.Plugins.Nano.ViewModels
{
    public class NanoListTransactionsViewModel : BasePagingViewModel
    {
        public class TransactionViewModel
        {
            public DateTimeOffset Timestamp { get; set; }
            // Nano doesn’t have fees or RBF. We only care if a block is cemented.
            public bool IsConfirmed { get; set; } // “cemented” in Nano terms

            public string Comment { get; set; }

            // Block or transaction hash (Nano = block hash)
            public string Id { get; set; }

            // Explorer link for convenience (e.g., NanoLooker/NanoCrawler)
            public string Link { get; set; }

            // True = incoming (receive), False = outgoing (send)
            public bool Positive { get; set; }

            // Display-friendly balance delta, e.g. “+1.234 NANO” or “-0.5 NANO”
            public string Amount { get; set; }

            // Optional counterparty address
            public string Address { get; set; }

            // Optional tags/labels for UX
            public List<string> Tags { get; set; } = new();

            // FX info (optional)
            public decimal? Rate { get; set; }           // fiat per 1 NANO at tx time
            public decimal? FiatAmount { get; set; }     // Amount * Rate (signed)
            public string FiatCode { get; set; } = "USD";
        }

        // Optional label palette for your UI (text + colors)
        public HashSet<(string Text, string Color, string TextColor)> Labels { get; set; } = new();

        public List<TransactionViewModel> Transactions { get; set; } = new();

        // Basic paging (self-contained; no BTCPay base classes)
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
        public int Total { get; set; }
        public override int CurrentPageCount => Transactions.Count;

        public string CryptoCode { get; set; } = "XNO";

        // For scoping links back to wallet pages
        public string WalletId { get; set; }

        // Optional: current wallet balance for header
        public string CurrentBalanceNano { get; set; }
    }
}