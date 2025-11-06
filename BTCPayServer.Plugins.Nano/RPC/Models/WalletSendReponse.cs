using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Nano.RPC.Models
{
    public class WalletSendResponse
    {
        [JsonProperty("block")] public string Block { get; set; }

    }
}