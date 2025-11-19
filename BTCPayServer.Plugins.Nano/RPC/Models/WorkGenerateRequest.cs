using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Nano.RPC.Models
{
    public class WorkGenerateRequest
    {
        [JsonProperty("hash")] public string Hash { get; set; }

    }
}