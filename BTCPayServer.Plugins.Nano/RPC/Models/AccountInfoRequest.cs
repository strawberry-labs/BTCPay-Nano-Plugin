using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Nano.RPC.Models
{
    public class AccountInfoRequest
    {
        [JsonProperty("account")] public string Account { get; set; }
        [JsonProperty("representative")] public bool Representative { get; set; } = true;
        [JsonProperty("pending")] public bool Pending { get; set; } = false;
        [JsonProperty("include_confirmed")] public bool IncludeConfirmed { get; set; } = true;
    }
}