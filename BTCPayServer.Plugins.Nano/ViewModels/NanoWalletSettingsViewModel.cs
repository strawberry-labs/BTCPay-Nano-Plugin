using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Nano.ViewModels
{
    // Standalone (no DerivationSchemeViewModel inheritance)
    public class NanoWalletSettingsViewModel
    {
        public string StoreId { get; set; }
        // public string StoreName { get; set; }
        // public string WalletId { get; set; }
        // Used by the view (title, routes, etc.)
        public string CryptoCode { get; set; } = "NANO";
        public string UriScheme { get; set; } = "nano";

        [Display(Name = "Enabled")]
        public bool Enabled { get; set; }

        [Display(Name = "Label")]
        public string Label { get; set; }

        public string PublicAddress { get; set; }

        // Keep names used by the view; for Nano this can be the primary account/address or pubkey
        // [Display(Name = "Primary account")]
        // public string DerivationScheme { get; set; }
        // public string DerivationSchemeInput { get; set; }

        // // Keep these to satisfy the view’s conditionals; default to false for Nano
        // public bool CanUsePayJoin { get; set; } = false;
        // public bool PayJoinEnabled { get; set; } = false;

        // // Multisig and PSBT are not applicable to Nano, but keep to satisfy the view
        // public bool CanSetupMultiSig { get; set; } = false;

        // [Display(Name = "Is MultiSig on Server")]
        // public bool IsMultiSigOnServer { get; set; } = false;

        // [Display(Name = "Default Include NonWitness Utxo in PSBTs")]
        // public bool DefaultIncludeNonWitnessUtxo { get; set; } = false;

        // public bool NBXSeedAvailable { get; set; } = false;

        // public List<WalletSettingsAccountKeyViewModel> AccountKeys { get; set; } = new();

        // public bool IsMultiSig => AccountKeys?.Count > 1;
    }

    public class WalletSettingsAccountKeyViewModel
    {
        // Keep property names used by the view and QR export; no BTC-specific validators
        [JsonProperty("ExtPubKey")]
        [Display(Name = "Account key")]
        public string AccountKey { get; set; }

        [Display(Name = "Master fingerprint")]
        public string MasterFingerprint { get; set; }

        [Display(Name = "Account key path")]
        public string AccountKeyPath { get; set; }
    }
}