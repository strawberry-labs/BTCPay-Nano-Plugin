using System;
using System.Collections.Generic;

namespace BTCPayServer.Plugins.Nano.Configuration
{
    public class NanoLikeConfiguration
    {
        public Dictionary<string, NanoLikeConfigurationItem> NanoLikeConfigurationItems { get; set; } = [];
    }

    public class NanoLikeConfigurationItem
    {
        public Uri DaemonRpcUri { get; set; }
        public Uri InternalWalletRpcUri { get; set; }
        public string WalletDirectory { get; set; }
        // TODO: Check if username password is necessary
        public string Username { get; set; }
        public string Password { get; set; }
        public Uri CashCowWalletRpcUri { get; set; }
    }
}