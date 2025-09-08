using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

using BTCPayServer.Plugins.Nano.Configuration;
using BTCPayServer.Plugins.Nano.RPC;
using BTCPayServer.Plugins.Nano.RPC.Models;
using BTCPayServer.Services;

using Microsoft.Extensions.Logging;

using NBitcoin;

namespace BTCPayServer.Plugins.Nano.Services
{
    public class NanoRPCProvider
    {
        private readonly NanoLikeConfiguration _nanoLikeConfiguration;
        private readonly ILogger<NanoRPCProvider> _logger;
        private readonly EventAggregator _eventAggregator;
        private readonly BTCPayServerEnvironment environment;
        public ImmutableDictionary<string, JsonRpcClient> RpcClients;
        // public ImmutableDictionary<string, JsonRpcClient> WalletRpcClients;

        private readonly ConcurrentDictionary<string, NanoLikeSummary> _summaries = new();

        public ConcurrentDictionary<string, NanoLikeSummary> Summaries => _summaries;

        public NanoRPCProvider(NanoLikeConfiguration nanoLikeConfiguration,
            ILogger<NanoRPCProvider> logger,
            EventAggregator eventAggregator,
            IHttpClientFactory httpClientFactory, BTCPayServerEnvironment environment)
        {
            _nanoLikeConfiguration = nanoLikeConfiguration;
            _logger = logger;
            _eventAggregator = eventAggregator;
            this.environment = environment;
            RpcClients =
                _nanoLikeConfiguration.NanoLikeConfigurationItems.ToImmutableDictionary(pair => pair.Key,
                    pair => new JsonRpcClient(pair.Value.RpcUri,
                        httpClientFactory.CreateClient($"{pair.Key}client")));

            // if (environment.CheatMode)
            // {
            //     CashCowWalletRpcClients =
            //         _nanoLikeConfiguration.NanoLikeConfigurationItems
            //             .Where(i => i.Value.CashCowWalletRpcUri is not null).ToImmutableDictionary(pair => pair.Key,
            //                 pair => new JsonRpcClient(pair.Value.CashCowWalletRpcUri, "", "",
            //                     httpClientFactory.CreateClient($"{pair.Key}cashcow-client")));
            // }
        }

        public ImmutableDictionary<string, JsonRpcClient> CashCowWalletRpcClients { get; set; }

        public bool IsConfigured(string cryptoCode) => RpcClients.ContainsKey(cryptoCode);
        public bool IsAvailable(string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            return _summaries.ContainsKey(cryptoCode) && IsAvailable(_summaries[cryptoCode]);
        }

        private bool IsAvailable(NanoLikeSummary summary)
        {
            return summary.Synced &&
                   summary.WalletAvailable;
        }

        public async Task<NanoLikeSummary> UpdateSummary(string cryptoCode)
        {
            if (!RpcClients.TryGetValue(cryptoCode.ToUpperInvariant(), out var RpcClient))
            {
                return null;
            }

            var summary = new NanoLikeSummary();
            try
            {
                var daemonResult =
                    await RpcClient.SendCommandAsync<JsonRpcClient.NoRequestModel, GetInfoResponse>("get_info",
                        JsonRpcClient.NoRequestModel.Instance);
                summary.TargetHeight = daemonResult.TargetHeight.GetValueOrDefault(0);
                summary.CurrentHeight = daemonResult.Height;
                summary.TargetHeight = summary.TargetHeight == 0 ? summary.CurrentHeight : summary.TargetHeight;
                summary.Synced = !daemonResult.BusySyncing;
                summary.UpdatedAt = DateTime.UtcNow;
                summary.DaemonAvailable = true;
            }
            catch
            {
                summary.DaemonAvailable = false;
            }

            bool walletCreated = false;
        retry:
            try
            {
                var walletResult =
                    await RpcClient.SendCommandAsync<JsonRpcClient.NoRequestModel, GetHeightResponse>(
                        "get_height", JsonRpcClient.NoRequestModel.Instance);
                summary.WalletHeight = walletResult.Height;
                summary.WalletAvailable = true;
            }
            catch when (environment.CheatMode && !walletCreated)
            {
                await CreateTestWallet(RpcClient);
                walletCreated = true;
                goto retry;
            }
            catch
            {
                summary.WalletAvailable = false;
            }

            // if (environment.CheatMode &&
            //     CashCowWalletRpcClients.TryGetValue(cryptoCode.ToUpperInvariant(), out var cashCow))
            // {
            //     await MakeCashCowFat(cashCow, daemonRpcClient);
            // }

            var changed = !_summaries.ContainsKey(cryptoCode) || IsAvailable(cryptoCode) != IsAvailable(summary);

            _summaries.AddOrReplace(cryptoCode, summary);
            if (changed)
            {
                _eventAggregator.Publish(new NanoDaemonStateChange() { Summary = summary, CryptoCode = cryptoCode });
            }

            return summary;
        }

        // private async Task MakeCashCowFat(JsonRpcClient cashcow, JsonRpcClient deamon)
        // {
        //     try
        //     {
        //         var walletResult =
        //             await cashcow.SendCommandAsync<JsonRpcClient.NoRequestModel, GetHeightResponse>(
        //                 "get_height", JsonRpcClient.NoRequestModel.Instance);
        //     }
        //     catch
        //     {
        //         _logger.LogInformation("Creating XNO cashcow wallet...");
        //         await CreateTestWallet(cashcow);
        //     }

        //     var balance =
        //         (await cashcow.SendCommandAsync<JsonRpcClient.NoRequestModel, GetBalanceResponse>("get_balance",
        //             JsonRpcClient.NoRequestModel.Instance));
        //     if (balance.UnlockedBalance != 0)
        //     {
        //         return;
        //     }
        //     _logger.LogInformation("Mining blocks for the cashcow...");
        //     var address = (await cashcow.SendCommandAsync<GetAddressRequest, GetAddressResponse>("get_address", new()
        //     {
        //         AccountIndex = 0
        //     })).Address;
        //     await deamon.SendCommandAsync<GenerateBlocks, JsonRpcClient.NoRequestModel>("generateblocks", new GenerateBlocks()
        //     {
        //         WalletAddress = address,
        //         AmountOfBlocks = 100
        //     });
        //     _logger.LogInformation("Mining succeed!");
        // }

        private static async Task CreateTestWallet(JsonRpcClient RpcClient)
        {
            try
            {
                await RpcClient.SendCommandAsync<OpenWalletRequest, JsonRpcClient.NoRequestModel>(
                    "open_wallet",
                    new OpenWalletRequest()
                    {
                        Filename = "wallet",
                        Password = "password"
                    });
                return;
            }
            catch
            {
                // ignored
            }

            await RpcClient.SendCommandAsync<CreateWalletRequest, JsonRpcClient.NoRequestModel>("create_wallet",
                new()
                {
                    Filename = "wallet",
                    Password = "password",
                    Language = "English"
                });
        }


        public class NanoDaemonStateChange
        {
            public string CryptoCode { get; set; }
            public NanoLikeSummary Summary { get; set; }
        }

        public class NanoLikeSummary
        {
            public bool Synced { get; set; }
            public long CurrentHeight { get; set; }
            public long WalletHeight { get; set; }
            public long TargetHeight { get; set; }
            public DateTime UpdatedAt { get; set; }
            public bool DaemonAvailable { get; set; }
            public bool WalletAvailable { get; set; }
        }
    }
}