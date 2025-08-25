using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Nano.RPC.Models
{
    public partial class OpenWalletErrorResponse
    {
        [JsonProperty("code")] public int Code { get; set; }
        [JsonProperty("message")] public string Message { get; set; }
    }
}