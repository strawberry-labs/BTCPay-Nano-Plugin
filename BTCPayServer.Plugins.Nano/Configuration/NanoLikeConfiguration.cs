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
        public Uri RpcUri { get; set; }
        public Uri WebsocketUri { get; set; }
    }
}