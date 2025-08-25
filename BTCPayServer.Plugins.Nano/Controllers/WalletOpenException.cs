using System;

namespace BTCPayServer.Plugins.Nano.Controllers;

public class WalletOpenException(string message) : Exception(message);