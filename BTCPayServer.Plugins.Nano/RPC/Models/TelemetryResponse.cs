using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Nano.RPC.Models
{
    public partial class TelemetryResponse
    {
        [JsonProperty("block_count")] public long BlockCount { get; set; }
        [JsonProperty("cemented_count")] public long CementedCount { get; set; }
        [JsonProperty("unchecked_count")] public long UncheckedCount { get; set; }
        [JsonProperty("account_count")] public long AccountCount { get; set; }
        // 0 means unlimited; otherwise bytes/second
        [JsonProperty("bandwidth_cap")] public long BandwidthCap { get; set; }

        [JsonProperty("peer_count")] public int PeerCount { get; set; }
        [JsonProperty("protocol_version")] public int ProtocolVersion { get; set; }

        // Seconds the node has been up
        [JsonProperty("uptime")] public long UptimeSeconds { get; set; }

        [JsonProperty("genesis_block")] public string GenesisBlock { get; set; } = string.Empty;

        [JsonProperty("major_version")] public int MajorVersion { get; set; }
        [JsonProperty("minor_version")] public int MinorVersion { get; set; }
        [JsonProperty("patch_version")] public int PatchVersion { get; set; }
        [JsonProperty("pre_release_version")] public int PreReleaseVersion { get; set; }
        [JsonProperty("maker")] public int Maker { get; set; }

        // Unix timestamp in milliseconds
        [JsonProperty("timestamp")] public long TimestampMs { get; set; }

        // Hex string
        [JsonProperty("active_difficulty")] public string ActiveDifficulty { get; set; } = string.Empty;
    }
}