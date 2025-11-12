using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Nano.RPC.Models
{
    public partial class AccountListRequest
    {
        [JsonProperty("wallet")] public string Wallet { get; set; }
    }
}