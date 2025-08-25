using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Nano.RPC.Models
{
    public partial class GetAccountsRequest
    {
        [JsonProperty("tag")] public string Tag { get; set; }
    }
}