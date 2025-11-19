using Newtonsoft.Json;
using System.Collections.Generic;

namespace BTCPayServer.Plugins.Nano.RPC.Models
{
    public partial class AccountListResponse
    {
        [JsonProperty("accounts")] public List<string> Accounts { get; set; }
    }
}