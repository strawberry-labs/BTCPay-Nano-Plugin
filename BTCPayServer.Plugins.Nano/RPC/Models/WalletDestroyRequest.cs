using System.Collections.Generic;

using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Nano.RPC.Models
{
    public partial class WalletDestroyRequest
    {
        [JsonProperty("wallet")] public string Wallet { get; set; }
    }
}