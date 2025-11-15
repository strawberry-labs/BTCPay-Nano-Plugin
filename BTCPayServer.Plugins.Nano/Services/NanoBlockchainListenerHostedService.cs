using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

using BTCPayServer.Logging;
using BTCPayServer.Plugins.Nano.Configuration;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using System.Numerics;
using BTCPayServer.Events;
using BTCPayServer.Plugins.Nano.RPC;
using BTCPayServer.Plugins.Nano.RPC.Models;
using BTCPayServer.Services.Stores;
using BTCPayServer.Payments;

namespace BTCPayServer.Plugins.Nano.Services
{
    public class NanoBlockchainListenerHostedService : IHostedService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly NanoRPCProvider _NanoRpcProvider;
        private readonly NanoLikeConfiguration _nanoLikeConfiguration;
        private readonly EventAggregator _eventAggregator;
        private readonly Uri _confirmationsWebSocketUri;
        // Address list + pollers
        private readonly object _addressesLock = new();
        private readonly List<AdhocAddress> _addresses = new();
        private readonly Dictionary<(string CryptoCode, string Address), CancellationTokenSource> _pollers = new();

        private readonly Dictionary<string, CancellationTokenSource> _walletReceivePollCts = new();
        private CancellationTokenSource _storePollingCts;

        // WS state
        private readonly object _wsStateLock = new();
        private ClientWebSocket _currentWebSocket;
        private readonly SemaphoreSlim _wsSendLock = new(1, 1);
        private Task _wsTask;
        private CancellationTokenSource _wsCts;

        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly List<Task> _runningTasks = new(); // pollers etc.
        private CancellationTokenSource _Cts;

        public Logs Logs { get; }

        public NanoBlockchainListenerHostedService(
            NanoRPCProvider nanoRpcProvider,
            NanoLikeConfiguration nanoLikeConfiguration,
            EventAggregator eventAggregator,
            IServiceScopeFactory scopeFactory,
            Logs logs)
        {
            _NanoRpcProvider = nanoRpcProvider;
            _nanoLikeConfiguration = nanoLikeConfiguration;
            _eventAggregator = eventAggregator;
            _scopeFactory = scopeFactory;
            Logs = logs;

            _confirmationsWebSocketUri = new Uri(
                Environment.GetEnvironmentVariable("BTCPAY_XNO_WEBSOCKET_URI") ?? "wss://rainstorm.city/websocket");
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _Cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            using var scope = _scopeFactory.CreateScope();
            var scopedStoreRepository = scope.ServiceProvider.GetRequiredService<StoreRepository>();
            var scopedNanoLikePaymentConfigService = scope.ServiceProvider.GetRequiredService<NanoLikePaymentConfigService>();

            // Start pollers for any pre-populated addresses (if any)
            foreach (var address in SnapshotAddresses())
                StartPollingForAddress(address);

            // Only start WS if we already have addresses
            if (SnapshotAddresses().Length > 0)
                EnsureWebSocketRunningIfNeeded();

            var stores = await scopedStoreRepository.GetStores();
            var pmi = PaymentTypes.CHAIN.GetPaymentMethodId("XNO");

            foreach (var storeData in stores) 
            {
                var storeId = storeData.Id;
                var cfg = await scopedNanoLikePaymentConfigService.getPaymentConfig(storeId, "XNO");

                if (cfg.Wallet != null) {
                    StartPollingWalletReceiveForStore(storeId);
                } else {
                    continue;
                }
            }

            StartPollingWalletReceiveForNewStore();   

            // return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                _Cts?.Cancel();

                // Stop WS loop
                await StopWebSocketIfRunningAsync().ConfigureAwait(false);

                // Stop all per-address pollers
                lock (_addressesLock)
                {
                    foreach (var cts in _pollers.Values)
                        cts.Cancel();
                }

                foreach (var walletCts in _walletReceivePollCts.Values)
                    walletCts.Cancel();

                _storePollingCts?.Cancel();

                if (_runningTasks.Count > 0)
                    await Task.WhenAll(_runningTasks).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            finally
            {
                _Cts?.Dispose();
                _storePollingCts?.Dispose();
            }
        }

        // Public API: add/remove/get addresses

        public bool AddAddress(AdhocAddress address)
        {
            Console.WriteLine("Adding Address");
            if (string.IsNullOrWhiteSpace(address.Address))
                return false;

            var newAccount = address.Address.Trim();
            bool added = false;
            bool firstAfterAdd = false;

            lock (_addressesLock)
            {
                if (!_addresses.Any(a => string.Equals(a.Address, newAccount, StringComparison.Ordinal)))
                {
                    _addresses.Add(address);
                    added = true;
                    if (_addresses.Count == 1) // first address overall
                        firstAfterAdd = true;
                }
            }

            if (!added)
                return false;

            Logs.PayServer.LogInformation("Added Nano address to subscription list: {Address}", newAccount);

            // Start polling failsafe for this address
            StartPollingForAddress(address);

            if (firstAfterAdd)
            {
                // Start/restart the WS connection (initial subscribe will send the full snapshot)
                Console.WriteLine("STarting WS Connection");
                EnsureWebSocketRunningIfNeeded();
            }
            else
            {
                // If already connected, send an update
                Console.WriteLine("Updating WS Connection " + newAccount);
                _ = TrySendUpdateAsync(accountsAdd: new[] { newAccount }, accountsDel: null);
                // string[] addresses = SnapshotAddresses().Select(account => account.Address).ToArray();
                // _ = TrySendUpdateAsync(accountsList: addresses);
            }

            return true;
        }

        public bool RemoveAddress(AdhocAddress address)
        {
            Console.WriteLine("In RemoveAddress");
            if (string.IsNullOrWhiteSpace(address.Address))
            {
                Console.WriteLine("In Here 1");
                return false;
            }

            var accountToRemove = address.Address.Trim();
            bool removed = false;
            bool nowEmpty = false;

            lock (_addressesLock)
            {
                removed = _addresses.Remove(address);
                var addresses = SnapshotAddresses();

                if (removed && _addresses.Count == 0)
                    nowEmpty = true;
            }

            if (!removed)
            {
                Console.WriteLine("In Here 1");
                return false;
            }

            Logs.PayServer.LogInformation("Removed Nano address from subscription list: {Address}", accountToRemove);

            // Stop polling failsafe for this address
            StopPollingForAddress(address);

            if (nowEmpty)
            {
                // Close the WS connection
                _ = StopWebSocketIfRunningAsync();
            }
            else
            {
                // If still connected, send an update
                _ = TrySendUpdateAsync(accountsAdd: null, accountsDel: new[] { address.Address });
                // string[] addresses = SnapshotAddresses().Select(account => account.Address).ToArray();
                // _ = TrySendUpdateAsync(accountsList: addresses);
            }

            return true;
        }

        public IReadOnlyCollection<AdhocAddress> GetAddresses()
        {
            lock (_addressesLock)
            {
                return _addresses.ToArray();
            }
        }

        // WebSocket lifecycle helpers

        private void EnsureWebSocketRunningIfNeeded()
        {
            // Only start if there are addresses
            if (SnapshotAddresses().Length == 0)
            {
                Console.WriteLine("Num addresses is 0");
                return;
            }


            lock (_wsStateLock)
            {
                if (_wsTask != null && !_wsTask.IsCompleted)
                    return;

                _wsCts?.Cancel();
                _wsCts?.Dispose();
                _wsCts = CancellationTokenSource.CreateLinkedTokenSource(_Cts.Token);

                _wsTask = Task.Run(() => ConnectAndListenConfirmationsWebSocketAsync(_wsCts.Token));
            }
        }

        private async Task StopWebSocketIfRunningAsync()
        {
            ClientWebSocket wsToClose = null;
            Task taskToWait = null;

            lock (_wsStateLock)
            {
                if (_wsTask == null || _wsTask.IsCompleted)
                    return;

                _wsCts?.Cancel();
                taskToWait = _wsTask;
                wsToClose = _currentWebSocket;
            }

            try
            {
                if (wsToClose != null &&
                    wsToClose.State == WebSocketState.Open)
                {
                    // Best-effort graceful close
                    using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    try
                    {
                        await wsToClose.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "No addresses to track", closeCts.Token)
                                        .ConfigureAwait(false);
                    }
                    catch { /* ignore */ }
                }
            }
            catch { /* ignore */ }

            try
            {
                if (taskToWait != null)
                    await taskToWait.ConfigureAwait(false);
            }
            catch { /* ignore, already closing */ }

            lock (_wsStateLock)
            {
                _wsTask = null;
                _wsCts?.Dispose();
                _wsCts = null;
            }
        }

        // WebSocket connect/subscribe/receive

        private async Task ConnectAndListenConfirmationsWebSocketAsync(CancellationToken ct)
        {
            var reconnectDelay = TimeSpan.FromSeconds(5);

            while (!ct.IsCancellationRequested)
            {
                using var ws = new ClientWebSocket();
                ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

                try
                {
                    // If we were asked to stop (e.g., last address removed), exit
                    if (SnapshotAddresses().Length == 0)
                        break;

                    Logs.PayServer.LogInformation("Connecting to Nano confirmations WebSocket at {Uri}", _confirmationsWebSocketUri);
                    await ws.ConnectAsync(_confirmationsWebSocketUri, ct).ConfigureAwait(false);
                    Interlocked.Exchange(ref _currentWebSocket, ws);
                    Logs.PayServer.LogInformation("WebSocket connected. State: {State}", ws.State);

                    // Initial subscribe with current address snapshot (can’t be empty here by design)
                    var accounts = SnapshotAddresses();
                    if (accounts.Length == 0)
                    {
                        // Edge case: became empty between check and subscribe; stop.
                        break;
                    }

                    await SendSubscribeAsync(ws, accounts, ct).ConfigureAwait(false);
                    Logs.PayServer.LogInformation("Subscribed to 'confirmation' topic for {Count} accounts", accounts.Length);

                    // Receive loop
                    var recvBuffer = new byte[16 * 1024];
                    var segment = new ArraySegment<byte>(recvBuffer);
                    var builder = new StringBuilder();

                    while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
                    {
                        builder.Clear();
                        WebSocketReceiveResult result;

                        do
                        {
                            result = await ws.ReceiveAsync(segment, ct).ConfigureAwait(false);
                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                Logs.PayServer.LogInformation("WebSocket close received: {Status} {Desc}", result.CloseStatus, result.CloseStatusDescription);
                                break;
                            }

                            var chunk = Encoding.UTF8.GetString(recvBuffer, 0, result.Count);
                            builder.Append(chunk);

                        } while (!result.EndOfMessage && !ct.IsCancellationRequested);

                        if (result.MessageType == WebSocketMessageType.Close)
                            break;

                        var message = builder.ToString();

                        try
                        {
                            using var doc = JsonDocument.Parse(message);
                            var root = doc.RootElement;
                            if (root.TryGetProperty("message", out var msg) &&
                                msg.TryGetProperty("account", out var account) &&
                                msg.TryGetProperty("hash", out var hash))
                            {
                                Logs.PayServer.LogInformation("WS confirmation: account={Account} hash={Hash}", account.GetString(), hash.GetString());

                                SendNanoEvent(root);
                            }
                            else
                            {
                                Logs.PayServer.LogDebug("WS message: {Message}", message);
                            }
                        }
                        catch (Exception ex)
                        {
                            Logs.PayServer.LogDebug(ex, "Failed parsing WS message: {Message}", message);
                        }
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (WebSocketException ex)
                {
                    Logs.PayServer.LogWarning(ex, "WebSocket error, reconnecting in {Delay}", reconnectDelay);
                }
                catch (Exception ex)
                {
                    Logs.PayServer.LogError(ex, "Unexpected error in confirmations WebSocket loop");
                }
                finally
                {
                    Interlocked.Exchange(ref _currentWebSocket, null)?.Dispose();
                }

                if (ct.IsCancellationRequested)
                    break;

                // If no more addresses, stop trying to reconnect
                if (SnapshotAddresses().Length == 0)
                    break;

                await Task.Delay(reconnectDelay, ct).ConfigureAwait(false);
            }

            Logs.PayServer.LogInformation("Confirmations WebSocket listener stopped.");
        }

        private async Task SendNanoEvent(JsonElement data)
        {
            try
            {
                if (!data.TryGetProperty("message", out var msg))
                    return;
                // Common fields
                var account = msg.TryGetProperty("account", out var a) ? a.GetString() : null;
                var hash = msg.TryGetProperty("hash", out var h) ? h.GetString() : null;
                // var subtype = msg.TryGetProperty("subtype", out var st) ? st.GetString() : null;
                var amountRawStr = msg.TryGetProperty("amount", out var amt) ? amt.GetString() : "0";

                // Extract block object (can be an object or a JSON string)
                JsonElement? blockObj = TryGetBlockObject(msg);

                // From block: destination (for sends) and source send hash (for receives)
                string linkAsAccount = null;
                string linkHash = null;
                string subtype = null;

                if (blockObj is JsonElement block)
                {
                    if (block.TryGetProperty("link_as_account", out var laa))
                        linkAsAccount = laa.GetString();
                    if (block.TryGetProperty("link", out var l))
                        linkHash = l.GetString();
                    if (block.TryGetProperty("subtype", out var s))
                        subtype = s.GetString();
                }

                // Snapshot of subscribed addresses
                var subscribed = new HashSet<AdhocAddress>(SnapshotAddresses(), AdhocAddressByAddressComparer.Instance);
                var accountIsOurs = !string.IsNullOrEmpty(account) && subscribed.Any(a => string.Equals(a.Address, account, StringComparison.Ordinal));
                var destinationIsOurs = !string.IsNullOrEmpty(linkAsAccount) && subscribed.Any(a => string.Equals(a.Address, linkAsAccount, StringComparison.Ordinal));

                if (accountIsOurs || destinationIsOurs)
                {
                    var accounts = SnapshotAddresses();

                }

                // Convert amount raw -> text nano for logging
                var amountNanoText = RawToNanoString(amountRawStr);

                // // Parse raw into long for event payload (best-effort; raw can exceed long)
                // long amountRaw = 0;
                // try { amountRaw = long.Parse(amountRawStr); } catch { /* ignore overflow; keep 0 */ }

                // Resolve CryptoCode (prefer any address we recognize in our pollers)
                var cryptoCode = ResolveCryptoCode(account, linkAsAccount) ?? _nanoLikeConfiguration
                    ?.NanoLikeConfigurationItems?.Keys?.FirstOrDefault() ?? "XNO";

                // Decide event kind and publish
                // - send: either external -> adhoc (SendToAdhocConfirmed) or adhoc -> store (SendToStoreWalletConfirmed)
                // - receive/open: receive on adhoc or (if subscribed) on store wallet
                if (string.Equals(subtype, "send", StringComparison.OrdinalIgnoreCase))
                {
                    if (destinationIsOurs && !accountIsOurs)
                    {

                        var addresses = GetAddresses();
                        AdhocAddress adhocAddress = addresses.Where(a => a.Address == linkAsAccount).ToArray()[0];
                        string storeId = adhocAddress.StoreId;

                        // External payer sent to our adhoc
                        var ev = new NanoEvent
                        {
                            CryptoCode = cryptoCode,
                            Kind = NanoEventKind.SendToAdhocConfirmed,
                            Account = linkAsAccount,       // our adhoc account
                            BlockHash = hash,
                            AmountRaw = amountRawStr,
                            FromAccount = account,
                            ToAccount = linkAsAccount,
                            StoreId = storeId
                        };

                        Logs.PayServer.LogInformation("Nano WS: SendToAdhocConfirmed from {From} to {To} amount(raw)={Raw} (~{Nano} NANO) hash={Hash}",
                            account, linkAsAccount, amountRawStr, amountNanoText, hash);
                        _eventAggregator?.Publish(ev);
                    }
                    else if (accountIsOurs && !string.IsNullOrEmpty(linkAsAccount))
                    {
                        // Our adhoc sent to store wallet (sweep)
                        var addresses = GetAddresses();
                        AdhocAddress adhocAddress = addresses.Where(a => a.Address == account).ToArray()[0];
                        string storeId = adhocAddress.StoreId;

                        var ev = new NanoEvent
                        {
                            CryptoCode = cryptoCode,
                            Kind = NanoEventKind.SendToStoreWalletConfirmed,
                            Account = account,            // our adhoc account
                            BlockHash = hash,
                            AmountRaw = amountRawStr,
                            FromAccount = account,
                            ToAccount = linkAsAccount,
                            StoreId = storeId
                        };

                        Logs.PayServer.LogInformation("Nano WS: SendToStoreWalletConfirmed from {From} to {To} amount(raw)={Raw} (~{Nano} NANO) hash={Hash}",
                            account, linkAsAccount, amountRawStr, amountNanoText, hash);
                        _eventAggregator?.Publish(ev);
                    }
                    // else: not relevant to our tracked addresses
                }
                else if (string.Equals(subtype, "receive", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(subtype, "open", StringComparison.OrdinalIgnoreCase))
                {
                    if (accountIsOurs)
                    {
                        var addresses = GetAddresses();
                        AdhocAddress adhocAddress = addresses.Where(a => a.Address == account).ToArray()[0];
                        string storeId = adhocAddress.StoreId;

                        var ev = new NanoEvent
                        {
                            CryptoCode = cryptoCode,
                            Kind = NanoEventKind.ReceiveOnAdhocConfirmed,
                            Account = account,            // the receiving account
                            BlockHash = hash,
                            AmountRaw = amountRawStr,
                            SourceSendHash = linkHash,
                            StoreId = storeId
                        };

                        Logs.PayServer.LogInformation("Nano WS: ReceiveOnAdhocConfirmed on {Account} amount(raw)={Raw} (~{Nano} NANO) hash={Hash} sourceSend={Source}",
                            account, amountRawStr, amountNanoText, hash, linkHash);
                        _eventAggregator?.Publish(ev);
                    }
                    // Currently no need to track confirmations on store wallet.
                    // Need to add additional logic.
                    // TODO: Configure receive on store wallet event. 

                    // else if (destinationIsOurs)
                    // {
                    //     var ev = new NanoEvent
                    //     {
                    //         CryptoCode = cryptoCode,
                    //         Kind = NanoEventKind.ReceiveOnStoreWalletConfirmed,
                    //         Account = account,            // the receiving account
                    //         BlockHash = hash,
                    //         AmountRaw = amountRawStr,
                    //         SourceSendHash = linkHash
                    //     };
                    //     Logs.PayServer.LogInformation("Nano WS: ReceiveOnStoreWalletConfirmed on {Account} amount(raw)={Raw} (~{Nano} NANO) hash={Hash} sourceSend={Source}",
                    //         account, amountRawStr, amountNanoText, hash, linkHash);
                    //     _eventAggregator?.Publish(ev);
                    // }
                }
                else
                {
                    // change/epoch/etc. not used
                    Logs.PayServer.LogDebug("Nano WS: Ignored subtype {Subtype} for account={Account} hash={Hash}", subtype, account, hash);
                }
            }
            catch (Exception ex)
            {
                Logs.PayServer.LogDebug(ex, "Failed processing Nano confirmation message");
            }

            await Task.CompletedTask;
        }

        private JsonElement? TryGetBlockObject(JsonElement msg)
        {
            if (!msg.TryGetProperty("block", out var blockEl))
                return null;
            if (blockEl.ValueKind == JsonValueKind.Object)
                return blockEl;

            if (blockEl.ValueKind == JsonValueKind.String)
            {
                var s = blockEl.GetString();
                if (!string.IsNullOrWhiteSpace(s) && s.TrimStart().StartsWith("{"))
                {
                    try
                    {
                        using var bd = JsonDocument.Parse(s);
                        return bd.RootElement.Clone();
                    }
                    catch { /* ignore */ }
                }
            }

            return null;
        }

        private string ResolveCryptoCode(params string[] addresses)
        {
            if (addresses == null || addresses.Length == 0)
                return null;
            lock (_addressesLock)
            {
                foreach (var addr in addresses)
                {
                    if (string.IsNullOrEmpty(addr))
                        continue;

                    foreach (var k in _pollers.Keys)
                    {
                        if (string.Equals(k.Address, addr, StringComparison.Ordinal))
                            return k.CryptoCode;
                    }
                }
            }
            return null;
        }

        private static string RawToNanoString(string rawStr)
        {
            if (string.IsNullOrWhiteSpace(rawStr))
                return "0";
            if (!BigInteger.TryParse(rawStr, out var raw))
                return "0";

            var unit = BigInteger.Pow(10, 30); // 1 NANO = 10^30 raw
            var integer = BigInteger.DivRem(raw, unit, out var remainder);

            if (remainder.IsZero)
                return integer.ToString();

            var frac = remainder.ToString().PadLeft(30, '0').TrimEnd('0');
            return $"{integer}.{frac}";
        }

        private async Task SendSubscribeAsync(ClientWebSocket ws, AdhocAddress[] accounts, CancellationToken ct)
        {
            var payload = new
            {
                action = "subscribe",
                topic = "confirmation",
                options = new
                {
                    accounts = accounts.Select(account => account.Address).ToArray()
                }
            };
            var json = JsonSerializer.Serialize(payload);
            var buffer = Encoding.UTF8.GetBytes(json);

            await _wsSendLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await ws.SendAsync(buffer, WebSocketMessageType.Text, endOfMessage: true, cancellationToken: ct).ConfigureAwait(false);
            }
            finally
            {
                _wsSendLock.Release();
            }
        }

        private async Task TrySendUpdateAsync(string[] accountsAdd = null, string[] accountsDel = null)
        {
            // Nothing to send
            var addEmpty = accountsAdd == null || accountsAdd.Length == 0;
            var delEmpty = accountsDel == null || accountsDel.Length == 0;
            if (addEmpty && delEmpty)
                return;
            // if (accountsList.Length == 0)

            var ws = _currentWebSocket;
            if (ws == null || ws.State != WebSocketState.Open)
                return;

            var payload = new UpdatePayload
            {
                action = "update",
                topic = "confirmation",
                ack = "true",
                options = new UpdateOptions
                {
                    accounts_add = addEmpty ? null : accountsAdd,
                    accounts_del = delEmpty ? null : accountsDel
                    // accounts = accountsList
                }
            };

            var json = JsonSerializer.Serialize(payload, _jsonOptions);

            var buffer = Encoding.UTF8.GetBytes(json);
            // var buffer = new StringContent(
            //         payload.ToString(Formatting.None),
            //         Encoding.UTF8, "application/json");

            try
            {
                await _wsSendLock.WaitAsync(_Cts?.Token ?? CancellationToken.None).ConfigureAwait(false);
                if (ws.State == WebSocketState.Open)
                    await ws.SendAsync(buffer, WebSocketMessageType.Text, true, _Cts?.Token ?? CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logs.PayServer.LogDebug(ex, "Failed sending WS update payload: {Json}", json);
            }
            finally
            {
                if (_wsSendLock.CurrentCount == 0)
                    _wsSendLock.Release();
            }
        }

        private AdhocAddress[] SnapshotAddresses()
        {
            lock (_addressesLock)
            {
                return _addresses.ToArray();
            }
        }

        // Manual polling failsafe

        // All this wallet polling is only for pippin
        private void StartPollingWalletReceiveForStore(string storeId) {
            var key = storeId;
            if (_walletReceivePollCts.ContainsKey(key))
                return;

            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_Cts.Token);

            _walletReceivePollCts[key] = linkedCts;

            // call pollwalletreceive
            var task = PollWalletReceive(linkedCts.Token, key);
            _runningTasks.Add(task);
        }

        private void StartPollingWalletReceiveForNewStore() {
            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_Cts.Token);

            _storePollingCts = linkedCts;

            var storePollTask = PollWalletReceiveForNewStore(linkedCts.Token);
            _runningTasks.Add(storePollTask);
        }

        private async Task PollWalletReceiveForNewStore(CancellationToken ct) {
            var delay = TimeSpan.FromMinutes(2);
            Logs.PayServer.LogInformation("Starting Store-Check Poll for Wallet Receive");

            while (!ct.IsCancellationRequested)
            {
                try {
                    using var scope = _scopeFactory.CreateScope();
                    var scopedStoreRepository = scope.ServiceProvider.GetRequiredService<StoreRepository>();
                    var scopedNanoLikePaymentConfigService = scope.ServiceProvider.GetRequiredService<NanoLikePaymentConfigService>();

                    var stores = await scopedStoreRepository.GetStores();

                    var storesIdsNotPolling = stores.Select((store) => store.Id).Where((id) => !_walletReceivePollCts.ContainsKey(id));

                    foreach (var storeId in storesIdsNotPolling) 
                    {
                        var cfg = await scopedNanoLikePaymentConfigService.getPaymentConfig(storeId, "XNO");

                        if (cfg.Wallet != null) {
                            StartPollingWalletReceiveForStore(storeId);
                        } else {
                            continue;
                        }
                    }
                } catch (Exception ex) {
                    Console.WriteLine(ex);
                }

                try
                {
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        private async Task PollWalletReceive(CancellationToken ct, string storeId) {
            var delay = TimeSpan.FromMinutes(2);
            Logs.PayServer.LogInformation("Starting wallet receive poll loop for storeId - " + storeId);

            while (!ct.IsCancellationRequested)
            {
                try {
                    using var scope = _scopeFactory.CreateScope();
                    var scopedNanoLikePaymentConfigService = scope.ServiceProvider.GetRequiredService<NanoLikePaymentConfigService>();
                    var config = await scopedNanoLikePaymentConfigService.getPaymentConfig(storeId, "XNO");
                    var walletId = config.Wallet;

                    var response = await _NanoRpcProvider.RpcClients["XNO"].SendCommandAsync<ReceiveAllRequest, ReceiveAllResponse>("receive_all", new ReceiveAllRequest { Wallet=walletId });

                } catch (Exception ex) {
                    Console.WriteLine(ex);
                }

                try
                {
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
            }

            Logs.PayServer.LogInformation("Stopped wallet receive poll loop for storeId - " + storeId);
        }

        private void StartPollingForAddress(AdhocAddress address)
        {
            lock (_addressesLock)
            {
                foreach (var kv in _nanoLikeConfiguration.NanoLikeConfigurationItems)
                {
                    var key = (kv.Key, address.Address);
                    if (_pollers.ContainsKey(key))
                        continue;

                    var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_Cts.Token);
                    _pollers[key] = linkedCts;

                    var task = PollConfirmationsLoopAsync(linkedCts.Token, kv.Key, address);
                    _runningTasks.Add(task);
                }
            }
        }

        private void StopPollingForAddress(AdhocAddress address)
        {
            lock (_addressesLock)
            {
                var keysToStop = _pollers.Keys.Where(k => k.Address == address.Address).ToList();
                foreach (var key in keysToStop)
                {
                    if (_pollers.TryGetValue(key, out var cts))
                    {
                        cts.Cancel();
                        cts.Dispose();
                        _pollers.Remove(key);
                    }
                }
            }
        }

        private async Task PollConfirmationsLoopAsync(CancellationToken ct, string cryptoCode, AdhocAddress address)
        {
            var delay = TimeSpan.FromSeconds(10);
            Logs.PayServer.LogInformation("Starting confirmations poll loop for {CryptoCode} address {Address}", cryptoCode, address.Address);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    Console.WriteLine("POLLING FOR ADDRESS " + address.Address);

                    var response = await _NanoRpcProvider.RpcClients[cryptoCode].SendCommandAsync<AccountsReceivableRequest, AccountsReceivableResponse>(
                                    "accounts_receivable", new AccountsReceivableRequest { Accounts = [address.Address], Count = "1", Source = "true" });

                    if (response.Blocks != null)
                    {
                        foreach (var accountEntry in response.Blocks)
                        {
                            string account = accountEntry.Key;
                            var blocks = accountEntry.Value;

                            foreach (var blockEntry in blocks)
                            {
                                string blockHash = blockEntry.Key;
                                string amount = blockEntry.Value.Amount;
                                string source = blockEntry.Value.Source;

                                // Do something with the data
                                // Console.WriteLine($"Account: {account}");
                                // Console.WriteLine($"Block: {blockHash}");
                                // Console.WriteLine($"Amount: {amount}");
                                // Console.WriteLine($"Source: {source}");
                                // Console.WriteLine();

                                var addresses = GetAddresses();
                                AdhocAddress adhocAddress = addresses.Where(a => a.Address == account).ToArray()[0];
                                string storeId = adhocAddress.StoreId;

                                // External payer sent to our adhoc
                                var ev = new NanoEvent
                                {
                                    CryptoCode = cryptoCode,
                                    Kind = NanoEventKind.SendToAdhocConfirmed,
                                    Account = account,       // our adhoc account
                                    BlockHash = blockHash,
                                    AmountRaw = amount,
                                    FromAccount = source,
                                    ToAccount = account,
                                    StoreId = storeId
                                };

                                Logs.PayServer.LogInformation("Nano Polling: SendToAdhocConfirmed from {From} to {To} amount(raw)={Raw} (~{Nano} NANO) hash={Hash}",
                                    source, account, amount, RawToNanoString(amount), blockHash);
                                _eventAggregator?.Publish(ev);
                            }
                        }
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logs.PayServer.LogError(ex, "Error polling confirmations for {Address}", address.Address);
                }

                try
                {
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
            }

            Logs.PayServer.LogInformation("Stopped confirmations poll loop for {CryptoCode} address {Address}", cryptoCode, address.Address);
        }

        // Payload helper types for JSON update
        private sealed class UpdatePayload
        {
            public string action { get; set; }
            public string topic { get; set; }
            public string ack { get; set; }
            public UpdateOptions options { get; set; }
        }

        private sealed class UpdateOptions
        {
            public string[] accounts_add { get; set; }
            public string[] accounts_del { get; set; }
            // public string[] accounts { get; set; }
        }

        public sealed class AdhocAddressByAddressComparer : IEqualityComparer<AdhocAddress>
        {
            public static readonly AdhocAddressByAddressComparer Instance = new();
            public bool Equals(AdhocAddress x, AdhocAddress y)
                => string.Equals(x?.Address, y?.Address, StringComparison.Ordinal);

            public int GetHashCode(AdhocAddress obj)
                => StringComparer.Ordinal.GetHashCode(obj?.Address ?? string.Empty);
        }
    }
}