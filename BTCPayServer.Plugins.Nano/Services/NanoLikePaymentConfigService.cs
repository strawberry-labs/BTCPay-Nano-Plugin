using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Services.Stores;

using BTCPayServer.Plugins.Nano.Configuration;

namespace BTCPayServer.Plugins.Nano.Services;

public class NanoLikePaymentConfigService
{
    private readonly StoreRepository _StoreRepository;

    public NanoLikePaymentConfigService(StoreRepository storeRepository)
    {
        _StoreRepository = storeRepository;
    }

    public async Task<NanoLikePaymentMethodConfiguration> getPaymentConfig(string storeId, string cryptoCode)
    {
        var code = string.IsNullOrWhiteSpace(cryptoCode) ? "XNO" : cryptoCode.ToUpperInvariant();
        var pmi = PaymentTypes.CHAIN.GetPaymentMethodId(code);
        var store = await _StoreRepository.FindStore(storeId);
        var cfg = store.GetPaymentMethodConfigs();

        if (cfg.TryGetValue(pmi, out var token) && token is JToken obj)
        {
            try
            {
                var nanoCfg = obj.ToObject<NanoLikePaymentMethodConfiguration>();

                return nanoCfg;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);

                return new NanoLikePaymentMethodConfiguration
                {
                    Wallet = null,
                    Enabled = false
                };
            }
        }
        else
        {
            return new NanoLikePaymentMethodConfiguration
            {
                Wallet = null,
                Enabled = false
            };
        }
    }

    public async Task setPaymentConfig(string storeId, string cryptoCode, NanoLikePaymentMethodConfiguration newConfig)
    {
        var store = await _StoreRepository.FindStore(storeId);

        if (store is null) throw new Exception("No Store Found");

        var code = string.IsNullOrWhiteSpace(cryptoCode) ? "XNO" : cryptoCode.ToUpperInvariant();
        var pmi = PaymentTypes.CHAIN.GetPaymentMethodId(code);

        if (newConfig == null)
        {
            Console.WriteLine("HERE SETTING CONFIG TO NULL");
            store.SetPaymentMethodConfig(pmi, null);
            return;
        }

        JObject jsonConfig = JObject.FromObject(newConfig);

        store.SetPaymentMethodConfig(pmi, jsonConfig);

        await _StoreRepository.UpdateStore(store);
    }
}