using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Nano.RPC.Models
{
    public partial class CreateAccountResponse
    {
        [JsonProperty("account")] public string Account { get; set; }
    }
}