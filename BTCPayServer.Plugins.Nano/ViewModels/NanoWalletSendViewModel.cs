using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Models;

namespace BTCPayServer.Plugins.Nano.ViewModels
{
    public class NanoWalletSendModel : IHasBackAndReturnUrl
    {
        // NANO: No BTC tri-state flags used here
        // public enum ThreeStateBool { Maybe, Yes, No }
        // NANO: No fee targets or sat/vB fee rates
        // public class FeeRateOption
        // {
        //     public TimeSpan Target { get; set; }
        //     public decimal FeeRate { get; set; }
        // }

        public List<TransactionOutput> Outputs { get; set; } = new();

        public class TransactionOutput
        {
            [Display(Name = "Destination Address")]
            [Required]
            public string DestinationAddress { get; set; }

            [Display(Name = "Amount")]
            [Required]
            // NANO: BTC amount range not applicable
            // [Range(1E-08, 21E6)]
            public string? Amount { get; set; }

            // NANO: No network fees to subtract from outputs
            // [Display(Name = "Subtract fees from amount")]
            // public bool SubtractFeesFromOutput { get; set; }

            // public string PayoutId { get; set; }

            // public string[] Labels { get; set; } = Array.Empty<string>();
        }

        public string CurrentBalance { get; set; }

        // NANO: No mining concept; if needed use a separate Pending/Receivable balance instead
        // public decimal ImmatureBalance { get; set; }

        public string CryptoCode { get; set; } = "XNO";

        // NANO: No fee recommendations
        // public List<FeeRateOption> RecommendedSatoshiPerByte { get; set; }

        // [Display(Name = "Fee rate (sat/vB)")]
        // [Required]
        // public decimal? FeeSatoshiPerByte { get; set; }

        // NANO: Account-based model, no change outputs
        // [Display(Name = "Don't create UTXO change")]
        // public bool NoChange { get; set; }

        public decimal? Rate { get; set; }
        public int FiatDivisibility { get; set; }
        public int CryptoDivisibility { get; set; }
        public string Fiat { get; set; }
        public string RateError { get; set; }

        // NANO: No PSBT/non-witness UTXO concept
        // [Display(Name = "Always include non-witness UTXO if available")]
        // public bool AlwaysIncludeNonWitnessUTXO { get; set; }

        // NANO: No NBXplorer seed
        // public bool NBXSeedAvailable { get; set; }

        // NANO: No BIP21/PayJoin
        // [Display(Name = "PayJoin BIP21")]
        // public string PayJoinBIP21 { get; set; }

        // NANO: No UTXO input selection
        // public bool InputSelection { get; set; }
        // public InputSelectionOption[] InputsAvailable { get; set; }
        // [Display(Name = "UTXOs to spend from")]
        // public IEnumerable<string> SelectedInputs { get; set; }

        public string BackUrl { get; set; }
        public string ReturnUrl { get; set; }

        // NANO: No multisig-on-server in standard flow
        // public bool IsMultiSigOnServer { get; set; }

        // NANO: UTXO selection details not applicable
        // public class InputSelectionOption
        // {
        //     public IEnumerable<TransactionTagModel> Labels { get; set; }
        //     public string Comment { get; set; }
        //     public decimal Amount { get; set; }
        //     public string Outpoint { get; set; }
        //     public string Link { get; set; }
        //     public long Confirmations { get; set; }
        //     public DateTimeOffset? Timestamp { get; set; }
        // }
    }
}