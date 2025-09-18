using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;

using BTCPayServer.Plugins.Nano.RPC.Models;
using BTCPayServer.Plugins.Nano.RPC;
using BTCPayServer.Plugins.Nano.Repositories;
using BTCPayServer.Plugins.Nano.Data;

namespace BTCPayServer.Plugins.Nano.Services;

public class NanoAdhocAddressService
{
    private readonly NanoRPCProvider _nanoRpcProvider;
    private readonly NanoLikeSpecificBtcPayNetwork _network;
    private readonly InvoiceAdhocAddressRepository _invoiceAdhocAddressRepository;
    private readonly IDataProtector _protector;

    public NanoAdhocAddressService(NanoLikeSpecificBtcPayNetwork network, NanoRPCProvider nanoRpcProvider, InvoiceAdhocAddressRepository invoiceAdhocAddressRepository, IDataProtectionProvider provider)
    {
        _network = network;
        _nanoRpcProvider = nanoRpcProvider;
        _invoiceAdhocAddressRepository = invoiceAdhocAddressRepository;
        // Purpose string should be unique and stable
        _protector = provider.CreateProtector("BTCPayServer.Plugins.Nano.AdhocKeys.v1");
    }

    async public Task<InvoiceAdhocAddress> PrepareAdhocAddress(string invoiceId)
    {
        Console.WriteLine("Preparing Adhoc Address");
        // Get the adhoc address
        KeyCreateResponse newAccount = await _nanoRpcProvider.RpcClients[_network.CryptoCode].SendCommandAsync<JsonRpcClient.NoRequestModel, KeyCreateResponse>("key_create", JsonRpcClient.NoRequestModel.Instance);
        // KeyCreateResponse newAccount = await _nanoRpcProvider.RpcClients["XNO"].SendCommandAsync<JsonRpcClient.NoRequestModel, KeyCreateResponse>("key_create", JsonRpcClient.NoRequestModel.Instance);

        string privKey = newAccount.Private;
        string pubKey = newAccount.Public;
        string account = newAccount.Account;

        var encryptedPrivKey = EncryptHex(privKey);

        // Save it in db with invoiceId and storeId
        InvoiceAdhocAddress adhocAddress = await _invoiceAdhocAddressRepository.AddAsync(pubKey, privKey, account, invoiceId);

        // return an object that has all relevant data. 
        return adhocAddress;
    }

    public string EncryptHex(string hex)
    => Encrypt(Convert.FromHexString(hex));

    public string DecryptToHex(string protectedBase64)
        => Convert.ToHexString(Decrypt(protectedBase64)).ToLowerInvariant();

    public string Encrypt(byte[] privateKey)
    => Convert.ToBase64String(_protector.Protect(privateKey));

    public byte[] Decrypt(string protectedBase64)
        => _protector.Unprotect(Convert.FromBase64String(protectedBase64));
}