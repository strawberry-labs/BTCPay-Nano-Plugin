using System;
using System.Collections.Generic;

namespace BTCPayServer.Plugins.Nano.Configuration
{
    public class NanoLikePaymentMethodConfiguration
    {
        public bool Enabled { get; set; } = false;
        public string Wallet { get; set; }

        public string PublicAddress { get; set; } = null;
    }

}