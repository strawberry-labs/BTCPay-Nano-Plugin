using System.Collections.Generic;

using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Nano.RPC.Models
{
    public partial class WalletDestroyResponse
    {
        [JsonProperty("destroyed")] public string Destroyed { get; set; }
    }
}