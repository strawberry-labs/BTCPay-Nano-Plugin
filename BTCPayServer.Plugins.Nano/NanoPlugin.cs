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
using BTCPayServer.Plugins.Nano.Configuration;
using BTCPayServer.Plugins.Nano.Payments;
using BTCPayServer.Plugins.Nano.Repositories;
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

        var network = new NanoLikeSpecificBtcPayNetwork()
        {
            CryptoCode = "XNO",
            DisplayName = "Nano",
            Divisibility = 6, // or up to 18 if you need higher precision
            DefaultRateRules = new[]
            {
                // If your source has XNO_BTC:
                "XNO_X = XNO_BTC * BTC_X",
                "XNO_BTC = binance(XNO_BTC)", // or binance/okx/etc. if supported
                // If BTC pair isn’t available, derive via USD:
                // "XNO_X = XNO_USD * USD_X",
                // "XNO_USD = coingecko(XNO_USD)"
            },
            CryptoImagePath = "nano.svg",
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

        services.AddSingleton<NanoLikePaymentConfigService>();

        services.AddHostedService<NanoLikeSummaryUpdaterHostedService>();

        services.AddHostedService<NanoListener>();

        services.AddSingleton<IUIExtension>(new UIExtension("StoreWalletsNavNanoExtension", "store-wallets-nav"));

        services.AddHostedService<ApplicationPartsLogger>();

        services.AddSingleton<IPaymentMethodHandler>(provider =>
                (IPaymentMethodHandler)ActivatorUtilities.CreateInstance(provider, typeof(NanoLikePaymentMethodHandler), new object[] { network }));

        services.AddSingleton<IPaymentLinkExtension>(provider =>
        (IPaymentLinkExtension)ActivatorUtilities.CreateInstance(provider, typeof(NanoPaymentLinkExtension), new object[] { network, pmi }));

        services.AddSingleton<ICheckoutModelExtension>(provider =>
        (ICheckoutModelExtension)ActivatorUtilities.CreateInstance(provider, typeof(NanoCheckoutModelExtension), new object[] { network, pmi }));

        services.AddScoped<InvoiceAdhocAddressRepository>();

        services.AddSingleton<NanoBlockchainListenerHostedService>();
        services.AddHostedService(sp => sp.GetRequiredService<NanoBlockchainListenerHostedService>());

        services.AddHostedService<PluginMigrationRunner>();
        services.AddSingleton(sp =>
        ActivatorUtilities.CreateInstance<NanoAdhocAddressService>(sp, network));
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

            var rpcUri =
                configuration.GetOrDefault<Uri>(
                    $"{nanoLikeSpecificBtcPayNetwork.CryptoCode}_rpc_uri", null);

            var websocketUri =
                configuration.GetOrDefault<Uri>(
                    $"{nanoLikeSpecificBtcPayNetwork.CryptoCode}_websocket_uri", null);

            if (rpcUri == null || websocketUri == null)
            {
                var logger = serviceProvider.GetRequiredService<ILogger<NanoPlugin>>();
                var cryptoCode = nanoLikeSpecificBtcPayNetwork.CryptoCode.ToUpperInvariant();
                if (rpcUri is null)
                {
                    logger.LogWarning($"BTCPAY_{cryptoCode}_RPC_URI is not configured");
                }

                if (websocketUri is null)
                {
                    logger.LogWarning($"BTCPAY_{cryptoCode}_WEBSOCKET_URI is not configured");
                }

                logger.LogWarning($"{cryptoCode} got disabled as it is not fully configured.");
            }
            else
            {
                result.NanoLikeConfigurationItems.Add(nanoLikeSpecificBtcPayNetwork.CryptoCode, new NanoLikeConfigurationItem()
                {
                    RpcUri = rpcUri,
                    WebsocketUri = websocketUri
                });
            }
        }
        return result;
    }
}
