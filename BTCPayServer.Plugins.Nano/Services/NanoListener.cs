using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Nano.Configuration;
using BTCPayServer.Plugins.Nano.Payments;
using BTCPayServer.Plugins.Nano.RPC;
using BTCPayServer.Plugins.Nano.Services;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;

using Microsoft.Extensions.Logging;

using Newtonsoft.Json.Linq;

using System.Numerics;
using Newtonsoft.Json.Linq;
using BTCPayServer.Plugins.Nano.RPC.Models;

// TODO: Implement Parallelization
namespace BTCPayServer.Plugins.Nano.Services
{
    public class NanoListener : EventHostedServiceBase
    {
        private readonly InvoiceRepository _invoiceRepository;
        private readonly EventAggregator _eventAggregator;
        private readonly NanoRPCProvider _nanoRpcProvider;
        private readonly NanoLikeConfiguration _nanoLikeConfiguration;
        private readonly NanoLikePaymentConfigService _nanoLikePaymentConfigService;
        private readonly BTCPayNetworkProvider _networkProvider;
        private readonly ILogger<NanoListener> _logger;
        private readonly PaymentMethodHandlerDictionary _handlers;
        private readonly InvoiceActivator _invoiceActivator;
        private readonly PaymentService _paymentService;
        private readonly NanoAdhocAddressService _nanoAdhocAddressService;
        private readonly NanoBlockchainListenerHostedService _nanoBlockchainListenerHostedService;
        // private readonly IBackgroundTaskQueue _paymentsTaskQueue;

        public NanoListener(
            InvoiceRepository invoiceRepository,
            EventAggregator eventAggregator,
            NanoRPCProvider nanoRpcProvider,
            NanoLikeConfiguration nanoLikeConfiguration,
            BTCPayNetworkProvider networkProvider,
            ILogger<NanoListener> logger,
            PaymentMethodHandlerDictionary handlers,
            InvoiceActivator invoiceActivator,
            NanoAdhocAddressService nanoAdhocAddressService,
            NanoLikePaymentConfigService nanoLikePaymentConfigService,
            NanoBlockchainListenerHostedService nanoBlockchainListenerHostedService,
            // IBackgroundTaskQueue paymentsTaskQueue,
            PaymentService paymentService) : base(eventAggregator, logger)
        {
            _invoiceRepository = invoiceRepository;
            _eventAggregator = eventAggregator;
            _nanoRpcProvider = nanoRpcProvider;
            _nanoLikeConfiguration = nanoLikeConfiguration;
            _networkProvider = networkProvider;
            _logger = logger;
            _handlers = handlers;
            _invoiceActivator = invoiceActivator;
            _paymentService = paymentService;
            _nanoAdhocAddressService = nanoAdhocAddressService;
            _nanoLikePaymentConfigService = nanoLikePaymentConfigService;
            _nanoBlockchainListenerHostedService = nanoBlockchainListenerHostedService;
            // _paymentsTaskQueue = paymentsTaskQueue;
        }

        protected override void SubscribeToEvents()
        {
            base.SubscribeToEvents();
            Subscribe<NanoEvent>();
            Subscribe<NanoRPCProvider.NanoDaemonStateChange>();
            Subscribe<InvoiceEvent>(); // handle sweep on expire if configured
        }

        protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
        {
            switch (evt)
            {
                case NanoRPCProvider.NanoDaemonStateChange stateChange:
                    await OnDaemonStateChange(stateChange);
                    break;

                case NanoEvent nanoEvent:
                    if (!_nanoRpcProvider.IsAvailable(nanoEvent.CryptoCode))
                        return;

                    await OnNanoEvent(nanoEvent, cancellationToken);
                    break;

                case InvoiceEvent invoiceEvent:
                    await OnInvoiceEvent(invoiceEvent, cancellationToken);
                    break;
            }
        }

        private Task OnDaemonStateChange(NanoRPCProvider.NanoDaemonStateChange stateChange)
        {
            if (_nanoRpcProvider.IsAvailable(stateChange.CryptoCode))
            {
                _logger.LogInformation("{CryptoCode} daemon just became available", stateChange.CryptoCode);
                // If you want to catch up any missed events, you can trigger a lightweight rescan here.
                // With a push confirmations service this is typically not required.
            }
            else
            {
                _logger.LogInformation("{CryptoCode} daemon just became unavailable", stateChange.CryptoCode);
            }

            return Task.CompletedTask;
        }

        private async Task OnNanoEvent(NanoEvent e, CancellationToken ct)
        {
            var cryptoCode = e.CryptoCode;
            var network = _networkProvider.GetNetwork(cryptoCode);
            var pmi = PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode);
            var handler = (NanoLikePaymentMethodHandler)_handlers[pmi];

            switch (e.Kind)
            {
                case NanoEventKind.SendToAdhocConfirmed:
                    _logger.LogInformation("Send to adhoc confirmed for {Account} amount(raw)={AmountRaw} sendHash={Hash}", e.ToAccount ?? e.Account, e.AmountRaw, e.BlockHash);
                    try
                    {
                        var adhoc = e.ToAccount ?? e.Account;
                        var invoiceIdOfAdhoc = await _nanoAdhocAddressService.GetInvoiceIdFromAccount(adhoc, ct);
                        if (!string.IsNullOrEmpty(adhoc))
                        {
                            Task.Run(async () =>
                        {
                            try
                            {
                                await RunWithRetriesAsync(async token => { await CreateReceiveBlockViaRpc(e, cryptoCode, adhoc, e.BlockHash, ct); }, maxRetries: 3, cancellationToken: ct);
                            }
                            catch (Exception ex)
                            {
                                // Log error
                                Console.WriteLine("Error - " + ex);
                            }
                        }, ct);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to create receive block on adhoc account for send {Hash}", e.BlockHash);
                    }
                    break;

                case NanoEventKind.SendToStoreWalletConfirmed:
                    _logger.LogInformation("Send to store wallet confirmed from {From} to {To} amount(raw)={AmountRaw} sendHash={Hash}", e.FromAccount, e.ToAccount, e.AmountRaw, e.BlockHash);
                    break;
                // try
                // {
                //     if (!string.IsNullOrEmpty(e.ToAccount))
                //     {
                //         await CreateReceiveBlockViaRpc(cryptoCode, e.ToAccount, e.BlockHash, ct);
                //     }
                // }
                // catch (Exception ex)
                // {
                //     _logger.LogError(ex, "Failed to create receive block on store wallet for send {Hash}", e.BlockHash);
                // }
                // break;

                case NanoEventKind.ReceiveOnAdhocConfirmed:
                    var invoiceId = await _nanoAdhocAddressService.GetInvoiceIdFromAccount(e.Account, ct);
                    // Funds are now received on the adhoc account. Record/update payment, then sweep if fully paid.
                    Task.Run(async () =>
                        {
                            try
                            {
                                await OnAdhocReceiveConfirmed(cryptoCode, pmi, handler, e, ct);
                            }
                            catch (Exception ex)
                            {
                                // Log error
                                Console.WriteLine("Error - " + ex);
                            }
                        }, ct);
                    break;

                    // case NanoEventKind.ReceiveOnStoreWalletConfirmed:
                    //     // Store wallet received the sweep; nothing to do for invoice accounting.
                    //     _logger.LogInformation("Store wallet receive confirmed on {Account} amount(raw)={AmountRaw} receiveHash={Hash}", e.Account, e.AmountRaw, e.BlockHash);
                    //     break;
            }
        }

        private async Task OnAdhocReceiveConfirmed(string cryptoCode, PaymentMethodId pmi, NanoLikePaymentMethodHandler handler, NanoEvent e, CancellationToken ct)
        {
            // Find the invoice by the adhoc address
            var invoiceId = await _nanoAdhocAddressService.GetInvoiceIdFromAccount(e.Account, ct);

            var invoice = await _invoiceRepository.GetInvoice(invoiceId);

            if (invoice == null)
            {
                _logger.LogWarning("Received funds on adhoc account {Account} but no matching invoice found. receiveHash={Hash}", e.Account, e.BlockHash);
                return;
            }

            // Build/update the payment entity for this receive
            var paymentDetails = new NanoLikePaymentData
            {
                AdhocAccount = e.Account,
                SendHash = e.SourceSendHash,      // may be null if not provided by feed; OK
                ReceiveHash = e.BlockHash,
                AmountRaw = e.AmountRaw,
                Confirmation = e.Confirmation
            };

            // Stable ID ties this payment to the specific inbound (prefer send hash, fallback to receive hash)
            var paymentId = !string.IsNullOrEmpty(e.SourceSendHash)
                ? $"{e.SourceSendHash}@{e.Account}"
                : $"{e.BlockHash}@{e.Account}";

            // // Check if already present
            var existing = GetAllNanoLikePayments(invoice, cryptoCode)
                .SingleOrDefault(p => p.Id == paymentId && p.PaymentMethodId == pmi);

            var amountDecimal = ConvertFromRaw(e.AmountRaw);

            // TODO: Check if it is "Settled" if the payment is not in full. 
            var paymentData = new PaymentData
            {
                Status = PaymentStatus.Settled, // confirmation topic indicates finality
                Amount = amountDecimal,
                Created = DateTimeOffset.UtcNow,
                Id = paymentId,
                Currency = cryptoCode,
                InvoiceDataId = invoice.Id,
            }.Set(invoice, handler, paymentDetails);

            var toPersist = new List<(PaymentEntity Payment, InvoiceEntity Invoice)>();

            if (existing == null)
            {
                var payment = await _paymentService.AddPayment(paymentData, ProofsFromNano(paymentDetails));
                if (payment != null)
                {
                    await ReceivedPayment(invoice, payment);
                }
            }
            else
            {
                existing.Status = PaymentStatus.Settled;
                existing.Details = JToken.FromObject(paymentDetails, handler.Serializer);
                toPersist.Add((existing, invoice));
                await _paymentService.UpdatePayments(toPersist.Select(t => t.Payment).ToList());
                foreach (var grp in toPersist.GroupBy(t => t.Invoice))
                    _eventAggregator.Publish(new InvoiceNeedUpdateEvent(grp.Key.Id));
            }

            // Re-load invoice to get updated payment calculation
            invoice = await _invoiceRepository.GetInvoice(invoice.Id);
            var prompt = invoice.GetPaymentPrompt(pmi);
            var details = handler.ParsePaymentPromptDetails(prompt.Details);

            var paymentConfig = await _nanoLikePaymentConfigService.getPaymentConfig(e.StoreId, cryptoCode);

            var walletAccount = paymentConfig.Account;

            await SweepAdhocToStoreWallet(e, cryptoCode, e.Account, walletAccount, ct);

            // If invoice fully paid, sweep adhoc balance to store wallet
            // if (prompt.Calculate().Due <= 0m)
            // {
            //     try
            //     {
            //         await SweepAdhocToStoreWallet(cryptoCode, details.AddressId, walletAddress, ct);
            //     }
            //     catch (Exception ex)
            //     {
            //         _logger.LogError(ex, "Failed to sweep adhoc {Adhoc} to store wallet {Wallet} for invoice {InvoiceId}", details.AddressId, walletAddress, invoice.Id);
            //     }
            // }
        }

        private async Task OnInvoiceEvent(InvoiceEvent evt, CancellationToken ct)
        {
            // Optionally sweep remaining balance on expiry. Gotta implement. Currently it automatically sends to wallet on every receive. 

            if (evt.Name == InvoiceEvent.Expired)
            {
                RemoveAddressFromWSListener(evt, ct);
                return;
            }
            else if (evt.Name == InvoiceEvent.Completed)
            {
                RemoveAddressFromWSListener(evt, ct);
                return;
            }
            else
            {
                // Require explicit opt-in in config
                // TODO: Set up sweep options in the future
                // var sweepOnExpire = _nanoLikeConfiguration?.SweepOnExpire ?? false;
                // var sweepOnExpire = true;
                // if (!sweepOnExpire)
                //     return;

                // var invoice = evt.Invoice;
                // var cryptoCode = evt.PaymentMethodId?.CryptoCode ?? null;
                // if (invoice == null || string.IsNullOrEmpty(cryptoCode))
                //     return;

                // var network = _networkProvider.GetNetwork(cryptoCode);
                // var pmi = PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode);
                // var handler = _handlers[pmi] as NanoLikePaymentMethodHandler;
                // if (handler == null)
                //     return;

                // var prompt = invoice.GetPaymentPrompt(pmi);
                // if (prompt?.Activated != true)
                //     return;

                // var details = handler.ParsePaymentPromptDetails(prompt.Details);
                // var walletAddress = "";
                // if (string.IsNullOrEmpty(details?.AddressId) || string.IsNullOrEmpty(walletAddress))
                //     return;

                // _logger.LogInformation("Invoice {InvoiceId} expired; sweeping any remaining balance from adhoc {Adhoc} to store wallet {Wallet}", invoice.Id, details.AdhocAccountAddress, details.StoreWalletAddress);

                // try
                // {
                //     await SweepAdhocToStoreWallet(cryptoCode, details.AdhocAccountAddress, details.StoreWalletAddress, ct);
                // }
                // catch (Exception ex)
                // {
                //     _logger.LogError(ex, "Failed to sweep on expiry for invoice {InvoiceId}", invoice.Id);
                // }
            }
        }

        private async Task RemoveAddressFromWSListener(InvoiceEvent evt, CancellationToken ct)
        {
            var invoice = evt.Invoice;
            var invoiceId = invoice.Id;
            var storeId = invoice.StoreId;
            var account = await _nanoAdhocAddressService.GetAccountFromInvoiceId(invoiceId, ct);

            _nanoBlockchainListenerHostedService.RemoveAddress(new AdhocAddress
            {
                Address = account,
                StoreId = storeId
            });
        }

        private async Task SweepAdhocToStoreWallet(NanoEvent e, string cryptoCode, string fromAdhoc, string toWallet, CancellationToken ct)
        {
            await RunWithRetriesAsync(async token => { await CreateSendBlockViaRpc(e, cryptoCode, fromAdhoc, toWallet, amountRaw: null, sweepAll: true, ct); }, maxRetries: 3, cancellationToken: ct);
        }

        private async Task ReceivedPayment(InvoiceEntity invoice, PaymentEntity payment)
        {
            _logger.LogInformation("Invoice {InvoiceId} received payment {Value} {Currency} {PaymentId}", invoice.Id, payment.Value, payment.Currency, payment.Id);

            var prompt = invoice.GetPaymentPrompt(payment.PaymentMethodId);
            if (prompt != null &&
                prompt.Activated &&
                prompt.Destination == payment.Destination &&
                prompt.Calculate().Due > 0.0m)
            {
                // await _invoiceActivator.ActivateInvoicePaymentMethod(invoice.Id, payment.PaymentMethodId, true);
                invoice = await _invoiceRepository.GetInvoice(invoice.Id);
            }

            _eventAggregator.Publish(new InvoiceEvent(invoice, InvoiceEvent.ReceivedPayment) { Payment = payment });
        }

        private static HashSet<string> ProofsFromNano(NanoLikePaymentData details)
        {
            // Reference hashes that prove provenance for this payment record
            var proofs = new HashSet<string>();
            if (!string.IsNullOrEmpty(details.SendHash)) proofs.Add(details.SendHash);
            if (!string.IsNullOrEmpty(details.ReceiveHash)) proofs.Add(details.ReceiveHash);
            return proofs;
        }

        private IEnumerable<PaymentEntity> GetAllNanoLikePayments(InvoiceEntity invoice, string cryptoCode)
        {
            return invoice.GetPayments(false)
                .Where(p => p.PaymentMethodId == PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode));
        }

        private async Task CreateReceiveBlockViaRpc(NanoEvent e, string cryptoCode, string account, string sourceSendHash, CancellationToken ct)
        {
            var rpc = _nanoRpcProvider.RpcClients[cryptoCode];

            // Get account info (to determine open vs receive and current state)
            AccountInfoResponse info = null;
            bool isOpened = true;

            try
            {
                info = await rpc.SendCommandAsync<AccountInfoRequest, AccountInfoResponse>(
                "account_info", new AccountInfoRequest { Account = account, Representative = true });
                if (!string.IsNullOrEmpty(info?.Error))
                {
                    isOpened = false;
                }

            }
            catch (Exception ex)
            {
                isOpened = false;
            }

            var amountRaw = e.AmountRaw ?? "0";

            // Representative
            var representative = isOpened
            ? info.Representative
            : await ResolveRepresentativeAsync(cryptoCode, account, ct);

            if (string.IsNullOrEmpty(representative))
                throw new InvalidOperationException($"No representative available for opening/receiving on {account}");

            // Balances and previous
            var previous = isOpened ? info.Frontier : "0";
            var prevBalance = isOpened ? (info.ConfirmedBalance ?? info.Balance ?? "0") : "0";
            var newBalance = RawAdd(prevBalance, amountRaw);

            // Work root: previous (if opened) else account public key (for open)
            // string workRoot;
            // if (isOpened)
            // {
            //     workRoot = previous;
            // }
            // else
            // {
            //     var acctKey = await rpc.SendCommandAsync<AccountKeyRequest, AccountKeyResponse>(
            //     "account_key", new AccountKeyRequest { Account = account });
            //     workRoot = acctKey.Key;
            // }

            // var work = await rpc.SendCommandAsync<WorkGenerateRequest, WorkGenerateResponse>(
            // "work_generate", new WorkGenerateRequest { Hash = workRoot });

            // if (string.IsNullOrEmpty(work?.Work))
            //     throw new InvalidOperationException("work_generate failed");

            // Private key to sign
            var privateKey = await _nanoAdhocAddressService.GetPrivateAddress(account, ct);

            if (string.IsNullOrEmpty(privateKey))
            {
                throw new InvalidOperationException($"No private key available for {account}");
            }

            // Create signed state block (subtype receive/open)
            var createReq = new BlockCreateRequest
            {
                Account = account,
                Previous = previous,
                Representative = representative,
                Balance = newBalance,
                Link = sourceSendHash, // receive uses source block hash
                Key = privateKey,
                // Work = work.Work,
                JsonBlock = true
            };
            var created = await rpc.SendCommandAsync<BlockCreateRequest, BlockCreateResponse>("block_create", createReq);
            if (created?.Block == null)
                throw new InvalidOperationException("block_create (receive/open) did not return a block");
            // // Process
            var processed = await rpc.SendCommandAsync<ProcessRequest, ProcessResponse>("process",
            new ProcessRequest { JsonBlock = true, Block = created.Block, Subtype = !isOpened ? "open" : "receive" });
            _logger.LogInformation("Processed receive/open block for {Account}. send={Source} newHash={Hash} newBalance={Bal}",
            account, sourceSendHash, processed?.Hash, newBalance);
        }

        private async Task<string> CreateSendBlockViaRpc(NanoEvent e, string cryptoCode, string fromAccount, string toAccount, string amountRaw, bool sweepAll, CancellationToken ct)
        {
            var rpc = _nanoRpcProvider.RpcClients[cryptoCode];
            // From account info
            var info = await rpc.SendCommandAsync<AccountInfoRequest, AccountInfoResponse>(
                "account_info", new AccountInfoRequest { Account = fromAccount, Representative = true });

            if (!string.IsNullOrEmpty(info?.Error))
                throw new InvalidOperationException($"account_info failed for {fromAccount}: {info.Error}");

            var previous = info.Frontier;
            var representative = info.Representative;
            var prevBalance = info.ConfirmedBalance ?? info.Balance ?? "0";

            // Destination public key
            var paymentConfig = await _nanoLikePaymentConfigService.getPaymentConfig(e.StoreId, cryptoCode);

            var destKey = paymentConfig.PublicAddress;

            // Determine amount/new balance
            string sendAmountRaw;
            if (sweepAll)
            {
                // Sweep all: leave 0
                sendAmountRaw = prevBalance;
            }
            else
            {
                sendAmountRaw = amountRaw ?? "0";
            }

            if (RawCompare(prevBalance, sendAmountRaw) < 0)
                throw new InvalidOperationException($"Insufficient funds: balance={prevBalance} < send={sendAmountRaw}");

            var newBalance = RawSub(prevBalance, sendAmountRaw);

            // if (string.IsNullOrEmpty(work?.Work))
            //     throw new InvalidOperationException("work_generate failed");

            // Sign
            var privateKey = await _nanoAdhocAddressService.GetPrivateAddress(fromAccount, ct);

            if (string.IsNullOrEmpty(privateKey))
                throw new InvalidOperationException($"No private key available for {fromAccount}");

            // Create signed send block (state)
            var createReq = new BlockCreateRequest
            {
                Account = fromAccount,
                Previous = previous,
                Representative = representative,
                Balance = newBalance,
                Link = destKey, // send uses destination pubkey in link
                Key = privateKey,
                JsonBlock = true
            };

            var created = await rpc.SendCommandAsync<BlockCreateRequest, BlockCreateResponse>("block_create", createReq);
            if (created?.Block == null)
                throw new InvalidOperationException("block_create (send) did not return a block");

            // Process
            var processed = await rpc.SendCommandAsync<ProcessRequest, ProcessResponse>("process",
                new ProcessRequest { JsonBlock = true, Block = created.Block, Subtype = "send" });

            _logger.LogInformation("Processed send block from {From} to {To}. amount(raw)={Amt} newHash={Hash} newBalance={Bal}",
                fromAccount, toAccount, sendAmountRaw, processed?.Hash, newBalance);

            return processed?.Hash;
        }

        private async Task<string> ResolveRepresentativeAsync(string cryptoCode, string account, CancellationToken ct)
        {
            // TODO: Handle representative handling. Get from config and let env var be passed, if not defaults to xnopay. 

            // // Prefer configured representative (per crypto)
            // var rep = _nanoLikeConfiguration?.GetRepresentativeForCrypto(cryptoCode);
            // if (!string.IsNullOrEmpty(rep))
            //     return rep;
            // // If not configured, you can optionally fall back to the store wallet's current representative
            // // (if you can resolve it from store context). Otherwise, require explicit config.
            // _logger.LogWarning("No representative configured for {Crypto}; please configure a representative. Using a safe default results in better network health.");
            return "nano_1xnopay1bfmyx5eit8ut4gg1j488kt8bjukijerbn37jh3wdm81y6mxjg8qj";
        }

        // TODO: Refactor these utils

        private static string RawAdd(string a, string b)
        {
            var ai = ParseRaw(a);
            var bi = ParseRaw(b);
            return (ai + bi).ToString();
        }

        private static string RawSub(string a, string b)
        {
            var ai = ParseRaw(a);
            var bi = ParseRaw(b);
            var res = ai - bi;
            if (res.Sign < 0) throw new InvalidOperationException("Negative balance");
            return res.ToString();
        }

        private static int RawCompare(string a, string b)
        {
            var ai = ParseRaw(a);
            var bi = ParseRaw(b);
            return ai.CompareTo(bi);
        }

        private static System.Numerics.BigInteger ParseRaw(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return System.Numerics.BigInteger.Zero;
            if (!System.Numerics.BigInteger.TryParse(s, out var v)) return System.Numerics.BigInteger.Zero;
            return v;
        }

        public decimal ConvertFromRaw(string amountRaw)
        {
            if (string.IsNullOrWhiteSpace(amountRaw))
                return 0m;

            bool negative = amountRaw[0] == '-';
            if (negative) amountRaw = amountRaw.Substring(1);

            amountRaw = amountRaw.TrimStart('0');
            if (amountRaw.Length == 0)
                return 0m;

            string s;
            if (amountRaw.Length <= 30)
            {
                var frac = amountRaw.PadLeft(30, '0').TrimEnd('0');
                s = frac.Length > 0 ? $"0.{frac}" : "0";
            }
            else
            {
                var intPart = amountRaw.Substring(0, amountRaw.Length - 30);
                var fracPart = amountRaw.Substring(amountRaw.Length - 30).TrimEnd('0');
                s = fracPart.Length > 0 ? $"{intPart}.{fracPart}" : intPart;
            }

            if (!decimal.TryParse(s, System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out var result))
                throw new OverflowException("Value too large to fit in decimal.");

            return negative ? -result : result;
        }

        public static async Task RunWithRetriesAsync(
        Func<CancellationToken, Task> task,
        int maxRetries = 3,
        TimeSpan? delayBetweenRetries = null,
        CancellationToken cancellationToken = default)
        {
            int attempt = 0;
            Exception lastException = null;

            delayBetweenRetries ??= TimeSpan.FromSeconds(2); // default delay

            while (attempt < maxRetries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await task(cancellationToken);
                    return; // success
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    attempt++;

                    Console.WriteLine($"[Retry {attempt}] Task failed: {ex.Message}");

                    if (attempt < maxRetries)
                    {
                        await Task.Delay(delayBetweenRetries.Value, cancellationToken);
                    }
                }
            }

            // Final failure after all retries
            Console.WriteLine($"Task failed after {maxRetries} attempts: {lastException?.Message}");
            throw lastException!;
        }
    }

}