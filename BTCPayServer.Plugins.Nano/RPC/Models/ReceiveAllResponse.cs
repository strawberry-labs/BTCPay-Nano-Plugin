using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Nano.RPC.Models
{
    public partial class ReceiveAllResponse
    {
        [JsonProperty("received")] public string Received { get; set; }
    }
}