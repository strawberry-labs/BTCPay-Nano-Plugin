using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Nano.RPC.Models
{
    public class WalletSendRequest
    {
        [JsonProperty("wallet")] public string Wallet { get; set; }
        [JsonProperty("source")] public string Source { get; set; }
        [JsonProperty("destination")] public string Destination { get; set; }
        [JsonProperty("amount")] public string Amount { get; set; }
        [JsonProperty("id")] public string Id { get; set; }

    }
}