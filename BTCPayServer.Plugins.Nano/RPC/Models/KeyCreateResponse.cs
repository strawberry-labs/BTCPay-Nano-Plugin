using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Nano.RPC.Models
{
    public partial class KeyCreateResponse
    {
        [JsonProperty("private")] public string Private { get; set; }
        [JsonProperty("public")] public string Public { get; set; }
        [JsonProperty("account")] public string Account { get; set; }
    }
}