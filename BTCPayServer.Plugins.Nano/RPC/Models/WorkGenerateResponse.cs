using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Nano.RPC.Models
{
    public class WorkGenerateResponse
    {
        [JsonProperty("work")] public string Work { get; set; }
    }
}