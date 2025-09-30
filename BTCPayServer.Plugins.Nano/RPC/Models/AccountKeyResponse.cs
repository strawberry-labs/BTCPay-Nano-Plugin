using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Nano.RPC.Models
{
    public class AccountKeyResponse
    {
        [JsonProperty("key")] public string Key { get; set; }
    }
}