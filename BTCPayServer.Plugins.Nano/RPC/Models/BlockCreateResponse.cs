using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Nano.RPC.Models
{
    public class BlockCreateResponse
    {
        [JsonProperty("hash")] public string Hash { get; set; }
        [JsonProperty("difficulty")] public string Difficulty { get; set; }
        [JsonProperty("block")] public NanoBlock Block { get; set; } // signed JSON state block
    }

    public class NanoBlock
    {
        [JsonProperty("type")] public string Type { get; set; } = "state";
        [JsonProperty("account")] public string Account { get; set; }
        [JsonProperty("previous")] public string Previous { get; set; } // "0" if open
        [JsonProperty("representative")] public string Representative { get; set; }
        [JsonProperty("balance")] public string Balance { get; set; } // raw string
        [JsonProperty("link")] public string Link { get; set; }       // 64-hex: send -> dest pubkey, receive -> source hash
        [JsonProperty("link_as_account")] public string LinkAsAccount { get; set; }
        [JsonProperty("signature")] public string Signature { get; set; }
        [JsonProperty("work")] public string Work { get; set; }       // PoW for root
    }
}