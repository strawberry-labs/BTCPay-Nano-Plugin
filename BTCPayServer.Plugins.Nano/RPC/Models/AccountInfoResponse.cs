using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Nano.RPC.Models
{
    public class AccountInfoResponse
    {
        [JsonProperty("frontier")] public string Frontier { get; set; }
        [JsonProperty("open_block")] public string OpenBlock { get; set; }
        [JsonProperty("representative")] public string Representative { get; set; }
        [JsonProperty("balance")] public string Balance { get; set; }
        [JsonProperty("confirmed_balance")] public string ConfirmedBalance { get; set; }
        [JsonProperty("error")] public string Error { get; set; }
    }
}