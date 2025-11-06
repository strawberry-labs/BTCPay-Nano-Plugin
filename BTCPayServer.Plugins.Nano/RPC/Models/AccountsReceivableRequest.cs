using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Nano.RPC.Models
{
    public class AccountsReceivableRequest
    {
        [JsonProperty("accounts")] public string[] Accounts { get; set; }
        [JsonProperty("count")] public string Count { get; set; }
        [JsonProperty("source")] public string Source { get; set; }
    }
}