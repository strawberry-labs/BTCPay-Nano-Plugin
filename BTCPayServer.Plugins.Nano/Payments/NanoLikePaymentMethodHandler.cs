using System;
using System.Threading.Tasks;

using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Nano.RPC.Models;
using BTCPayServer.Plugins.Nano.RPC;
using BTCPayServer.Plugins.Nano.Services;
using BTCPayServer.Plugins.Nano.Data;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Nano.Payments
{
    public class NanoLikePaymentMethodHandler : IPaymentMethodHandler
    {
        private readonly NanoLikeSpecificBtcPayNetwork _network;
        public NanoLikeSpecificBtcPayNetwork Network => _network;
        public JsonSerializer Serializer { get; }
        private readonly NanoRPCProvider _nanoRpcProvider;
        private readonly NanoAdhocAddressService _nanoAdhocAddressService;
        private readonly NanoBlockchainListenerHostedService _nanoBlockchainListenerHostedService;

        public PaymentMethodId PaymentMethodId { get; }

        public NanoLikePaymentMethodHandler(NanoLikeSpecificBtcPayNetwork network, NanoRPCProvider nanoRpcProvider, NanoAdhocAddressService nanoAdhocAddressService, NanoBlockchainListenerHostedService nanoBlockchainListenerHostedService)
        {
            PaymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode);
            _network = network;
            Serializer = BlobSerializer.CreateSerializer().Serializer;
            _nanoRpcProvider = nanoRpcProvider;
            _nanoAdhocAddressService = nanoAdhocAddressService;
            _nanoBlockchainListenerHostedService = nanoBlockchainListenerHostedService;
        }
        bool IsReady() => _nanoRpcProvider.IsConfigured(_network.CryptoCode) && _nanoRpcProvider.IsAvailable(_network.CryptoCode);

        public Task BeforeFetchingRates(PaymentMethodContext context)
        {
            context.Prompt.Currency = _network.CryptoCode;
            context.Prompt.Divisibility = _network.Divisibility;
            if (context.Prompt.Activated && IsReady())
            {
                var supportedPaymentMethod = ParsePaymentMethodConfig(context.PaymentMethodConfig);
                // var walletClient = _nanoRpcProvider.WalletRpcClients[_network.CryptoCode];
                var daemonClient = _nanoRpcProvider.RpcClients[_network.CryptoCode];
                var invoice = context.InvoiceEntity;
                try
                {
                    context.State = new Prepare()
                    {
                        // GetFeeRate = daemonClient.SendCommandAsync<GetFeeEstimateRequest, GetFeeEstimateResponse>("get_fee_estimate", new GetFeeEstimateRequest()),
                        ReserveAddress = async s =>
                        {
                            InvoiceAdhocAddress adhocAddress = await _nanoAdhocAddressService.PrepareAdhocAddress(invoice.Id);
                            _nanoBlockchainListenerHostedService.AddAddress(new AdhocAddress
                            {
                                Address = adhocAddress.account,
                                StoreId = invoice.StoreId
                            });

                            return adhocAddress;
                        },
                        // AccountIndex = supportedPaymentMethod.AccountIndex
                    };
                }
                catch (Exception ex)
                {
                    context.Logs.Write($"Error in BeforeFetchingRates: {ex.Message}", InvoiceEventData.EventSeverity.Error);
                }
            }
            return Task.CompletedTask;
        }

        public async Task ConfigurePrompt(PaymentMethodContext context)
        {
            if (!_nanoRpcProvider.IsConfigured(_network.CryptoCode))
            {
                throw new PaymentMethodUnavailableException($"BTCPAY_XNO_RPC_URI isn't configured");
            }

            if (!_nanoRpcProvider.IsAvailable(_network.CryptoCode) || context.State is not Prepare nanoPrepare)
            {
                throw new PaymentMethodUnavailableException($"Node or wallet not available");
            }

            var invoice = context.InvoiceEntity;
            // var feeRatePerKb = await nanoPrepare.GetFeeRate;
            var address = await nanoPrepare.ReserveAddress(invoice.Id);

            // var feeRatePerByte = feeRatePerKb.Fee / 1024;
            var details = new NanoLikeOnChainPaymentMethodDetails()
            {
                // AccountIndex = nanoPrepare.AccountIndex,
                AddressId = address.id,
                InvoiceSettledConfirmation = ParsePaymentMethodConfig(context.PaymentMethodConfig).InvoiceSettledConfirmation
            };
            context.Prompt.Destination = address.account;
            // context.Prompt.PaymentMethodFee = NanoMoney.Convert(feeRatePerByte * 100);
            context.Prompt.Details = JObject.FromObject(details, Serializer);
            context.TrackedDestinations.Add(address.account);
        }

        private NanoPaymentPromptDetails ParsePaymentMethodConfig(JToken config)
        {
            return config.ToObject<NanoPaymentPromptDetails>(Serializer) ?? throw new FormatException($"Invalid {nameof(NanoLikePaymentMethodHandler)}");
        }
        object IPaymentMethodHandler.ParsePaymentMethodConfig(JToken config)
        {
            return ParsePaymentMethodConfig(config);
        }

        class Prepare
        {
            // public Task<GetFeeEstimateResponse> GetFeeRate;
            public Func<string, Task<InvoiceAdhocAddress>> ReserveAddress;

            // public long AccountIndex { get; internal set; }
        }

        public NanoLikeOnChainPaymentMethodDetails ParsePaymentPromptDetails(JToken details)
        {
            return details.ToObject<NanoLikeOnChainPaymentMethodDetails>(Serializer);
        }
        object IPaymentMethodHandler.ParsePaymentPromptDetails(JToken details)
        {
            return ParsePaymentPromptDetails(details);
        }

        public NanoLikePaymentData ParsePaymentDetails(JToken details)
        {
            return details.ToObject<NanoLikePaymentData>(Serializer) ?? throw new FormatException($"Invalid {nameof(NanoLikePaymentMethodHandler)}");
        }
        object IPaymentMethodHandler.ParsePaymentDetails(JToken details)
        {
            return ParsePaymentDetails(details);
        }
    }
}
