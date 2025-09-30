using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Nano.RPC.Models
{
    public class ProcessResponse
    {
        [JsonProperty("hash")] public string Hash { get; set; }
    }
}