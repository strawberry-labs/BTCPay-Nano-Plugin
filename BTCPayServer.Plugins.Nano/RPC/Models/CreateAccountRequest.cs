using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Nano.RPC.Models
{
    public partial class CreateAccountRequest
    {
        [JsonProperty("wallet")] public string Wallet { get; set; }
    }
}