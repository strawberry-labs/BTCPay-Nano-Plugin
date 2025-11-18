using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Nano.RPC.Models
{
    public class BlockCreateRequest
    {
        [JsonProperty("type")] public string Type { get; set; } = "state";
        [JsonProperty("account")] public string Account { get; set; }
        [JsonProperty("previous")] public string Previous { get; set; } // "0" if open
        [JsonProperty("representative")] public string Representative { get; set; }
        [JsonProperty("balance")] public string Balance { get; set; } // raw string
        [JsonProperty("link")] public string Link { get; set; }       // 64-hex: send -> dest pubkey, receive -> source hash
        [JsonProperty("key")] public string Key { get; set; }         // private key hex (node signs)
        [JsonProperty("work")] public string Work { get; set; }       // PoW for root
        [JsonProperty("json_block")] public bool JsonBlock { get; set; } = true;
    }
}