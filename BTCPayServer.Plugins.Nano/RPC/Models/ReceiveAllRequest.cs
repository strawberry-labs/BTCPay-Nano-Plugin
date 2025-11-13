using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Nano.RPC.Models
{
    public partial class ReceiveAllRequest
    {
        [JsonProperty("wallet")] public string Wallet { get; set; }
    }
}