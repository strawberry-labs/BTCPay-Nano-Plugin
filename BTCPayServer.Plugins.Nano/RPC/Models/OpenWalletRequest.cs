using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Nano.RPC.Models
{
    public partial class OpenWalletRequest
    {
        [JsonProperty("filename")] public string Filename { get; set; }
        [JsonProperty("password")] public string Password { get; set; }
    }
}