using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Nano.RPC.Models
{
    // account_key (account -> pubkey)
    public class AccountKeyRequest
    {
        [JsonProperty("account")] public string Account { get; set; }
    }
}