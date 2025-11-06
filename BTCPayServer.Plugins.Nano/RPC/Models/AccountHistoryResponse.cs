using Newtonsoft.Json;
using System.Collections.Generic;

namespace BTCPayServer.Plugins.Nano.RPC.Models
{
    public class AccountHistoryResponse
    {
        [JsonProperty("account")] public string Account { get; set; }
        [JsonProperty("previous")] public string Previous { get; set; }
        [JsonProperty("history")] public List<NanoTransaction> History { get; set; } // signed JSON state block
    }

    public class NanoTransaction
    {
        [JsonProperty("type")] public string Type { get; set; } = "state";
        [JsonProperty("account")] public string Account { get; set; }
        [JsonProperty("amount")] public string Amount { get; set; }
        [JsonProperty("local_timestamp")] public string Local_Timestamp { get; set; }
        [JsonProperty("height")] public string Height { get; set; }
        [JsonProperty("hash")] public string Hash { get; set; }
        [JsonProperty("confirmed")] public string Confirmed { get; set; }

    }
}