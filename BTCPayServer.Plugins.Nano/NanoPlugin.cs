using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;

using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Abstractions.Services;
using BTCPayServer.Configuration;
using BTCPayServer.Hosting;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Nano.Services;
using BTCPayServer.Plugins.Nano.Services;
using BTCPayServer.Plugins.Nano.Configuration;
using BTCPayServer.Services;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using NBitcoin;

using NBXplorer;

using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;

namespace BTCPayServer.Plugins.Nano;

public class NanoPlugin : BaseBTCPayServerPlugin
{
    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    {
        new IBTCPayServerPlugin.PluginDependency { Identifier = nameof(BTCPayServer), Condition = ">=2.1.0" }
    };

    public override void Execute(IServiceCollection services)
    {
        var pluginServices = (PluginServiceCollection)services;
        var prov = pluginServices.BootstrapServices.GetRequiredService<NBXplorerNetworkProvider>();
        var chainName = prov.NetworkType;

        Console.WriteLine("ABCD", chainName);

        var network = new NanoLikeSpecificBtcPayNetwork()
        {
            CryptoCode = "XNO",
            DisplayName = "Nano",
            Divisibility = 12,
            DefaultRateRules = new[]
            {
                    "XMR_X = XMR_BTC * BTC_X",
                    "XMR_BTC = kraken(XMR_BTC)"
                },
            CryptoImagePath = "monero.svg",
            UriScheme = "nano"
        };

        var blockExplorerLink = chainName == ChainName.Mainnet
            ? "https://www.exploremonero.com/transaction/{0}"
            : "https://testnet.xmrchain.net/tx/{0}";

        var pmi = PaymentTypes.CHAIN.GetPaymentMethodId("XNO");
        services.AddDefaultPrettyName(pmi, network.DisplayName);
        services.AddBTCPayNetwork(network)
                .AddTransactionLinkProvider(pmi, new SimpleTransactionLinkProvider(blockExplorerLink));

        services.AddSingleton(provider =>
                ConfigureNanoLikeConfiguration(provider));

        services.AddSingleton<NanoRPCProvider>();

        services.AddSingleton<IUIExtension>(new UIExtension("StoreWalletsNavNanoExtension", "store-wallets-nav"));

        services.AddHostedService<ApplicationPartsLogger>();
        services.AddHostedService<PluginMigrationRunner>();
        services.AddSingleton<MyPluginService>();
        services.AddSingleton<MyPluginDbContextFactory>();
        services.AddDbContext<MyPluginDbContext>((provider, o) =>
        {
            var factory = provider.GetRequiredService<MyPluginDbContextFactory>();
            factory.ConfigureBuilder(o);
        });
    }

    class SimpleTransactionLinkProvider : DefaultTransactionLinkProvider
    {
        public SimpleTransactionLinkProvider(string blockExplorerLink) : base(blockExplorerLink)
        {
        }

        public override string GetTransactionLink(string paymentId)
        {
            if (string.IsNullOrEmpty(BlockExplorerLink))
            {
                return null;
            }
            return string.Format(CultureInfo.InvariantCulture, BlockExplorerLink, paymentId);
        }
    }

    private static NanoLikeConfiguration ConfigureNanoLikeConfiguration(IServiceProvider serviceProvider)
    {
        var configuration = serviceProvider.GetService<IConfiguration>();
        var btcPayNetworkProvider = serviceProvider.GetService<BTCPayNetworkProvider>();
        var result = new NanoLikeConfiguration();

        var supportedNetworks = btcPayNetworkProvider.GetAll()
            .OfType<NanoLikeSpecificBtcPayNetwork>();

        foreach (var nanoLikeSpecificBtcPayNetwork in supportedNetworks)
        {
            var daemonUri =
                configuration.GetOrDefault<Uri>($"{nanoLikeSpecificBtcPayNetwork.CryptoCode}_daemon_uri",
                    null);
            var walletDaemonUri =
                configuration.GetOrDefault<Uri>(
                    $"{nanoLikeSpecificBtcPayNetwork.CryptoCode}_wallet_daemon_uri", null);
            var cashCowWalletDaemonUri =
                configuration.GetOrDefault<Uri>(
                    $"{nanoLikeSpecificBtcPayNetwork.CryptoCode}_cashcow_wallet_daemon_uri", null);
            var walletDaemonWalletDirectory =
                configuration.GetOrDefault<string>(
                    $"{nanoLikeSpecificBtcPayNetwork.CryptoCode}_wallet_daemon_walletdir", null);
            var daemonUsername =
                configuration.GetOrDefault<string>(
                    $"{nanoLikeSpecificBtcPayNetwork.CryptoCode}_daemon_username", null);
            var daemonPassword =
                configuration.GetOrDefault<string>(
                    $"{nanoLikeSpecificBtcPayNetwork.CryptoCode}_daemon_password", null);
            if (daemonUri == null || walletDaemonUri == null)
            {
                var logger = serviceProvider.GetRequiredService<ILogger<NanoPlugin>>();
                var cryptoCode = nanoLikeSpecificBtcPayNetwork.CryptoCode.ToUpperInvariant();
                if (daemonUri is null)
                {
                    logger.LogWarning($"BTCPAY_{cryptoCode}_DAEMON_URI is not configured");
                }
                if (walletDaemonUri is null)
                {
                    logger.LogWarning($"BTCPAY_{cryptoCode}_WALLET_DAEMON_URI is not configured");
                }
                logger.LogWarning($"{cryptoCode} got disabled as it is not fully configured.");
            }
            else
            {
                result.NanoLikeConfigurationItems.Add(nanoLikeSpecificBtcPayNetwork.CryptoCode, new NanoLikeConfigurationItem()
                {
                    DaemonRpcUri = daemonUri,
                    Username = daemonUsername,
                    Password = daemonPassword,
                    InternalWalletRpcUri = walletDaemonUri,
                    WalletDirectory = walletDaemonWalletDirectory,
                    CashCowWalletRpcUri = cashCowWalletDaemonUri,
                });
            }
        }
        return result;
    }
}
