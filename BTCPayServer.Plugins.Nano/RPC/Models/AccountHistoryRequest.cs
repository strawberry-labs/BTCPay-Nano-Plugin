using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Nano.RPC.Models
{
    public class AccountHistoryRequest
    {
        [JsonProperty("account")] public string Account { get; set; }
        [JsonProperty("count")] public string Count { get; set; }
        [JsonProperty("head")] public string Head { get; set; }

        public bool ShouldSerializeHead() => this.Head != null;
    }
}