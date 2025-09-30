using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Nano.RPC.Models
{
    public class ProcessRequest
    {
        [JsonProperty("json_block")] public bool JsonBlock { get; set; } = true;
        [JsonProperty("block")] public NanoBlock Block { get; set; }
        // subtype is optional; node derives from state contents, but including helps logs/analytics.
        [JsonProperty("subtype")] public string Subtype { get; set; }
    }
}