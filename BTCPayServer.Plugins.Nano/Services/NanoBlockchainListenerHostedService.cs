using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using BTCPayServer.Logging;
using BTCPayServer.Plugins.Nano.Configuration;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.Nano.Services
{
    public class NanoBlockchainListenerHostedService : IHostedService
    {
        private readonly NanoRPCProvider _NanoRpcProvider;
        private readonly NanoLikeConfiguration _nanoLikeConfiguration;
        private readonly Uri _confirmationsWebSocketUri;
        // Address list + pollers
        private readonly object _addressesLock = new();
        private readonly List<string> _addresses = new()
        {
            "nano_11frmgxjp8kacwi6ucpu9g6go9o7k1oojwzep3aca76pca7nujkpz79r17mb"
        };
        private readonly Dictionary<(string CryptoCode, string Address), CancellationTokenSource> _pollers = new();

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
            Logs logs)
        {
            _NanoRpcProvider = nanoRpcProvider;
            _nanoLikeConfiguration = nanoLikeConfiguration;
            Logs = logs;

            _confirmationsWebSocketUri = new Uri(
                Environment.GetEnvironmentVariable("BTCPAY_XNO_WEBSOCKET_URI") ?? "wss://rainstorm.city/websocket");
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _Cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // Start pollers for any pre-populated addresses (if any)
            foreach (var address in SnapshotAddresses())
                StartPollingForAddress(address);

            // Only start WS if we already have addresses
            if (SnapshotAddresses().Length > 0)
                EnsureWebSocketRunningIfNeeded();

            AddAddress("nano_1u37ahnhujqkxcjwp9hizkybtwu9xs7bz71qs6uq4myou86g7s36jzs69nx7");
            RemoveAddress("nano_11frmgxjp8kacwi6ucpu9g6go9o7k1oojwzep3aca76pca7nujkpz79r17mb");
            return Task.CompletedTask;
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

                if (_runningTasks.Count > 0)
                    await Task.WhenAll(_runningTasks).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            finally
            {
                _Cts?.Dispose();
                _currentWebSocket?.Dispose();
            }
        }

        // Public API: add/remove/get addresses

        public bool AddAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                return false;

            address = address.Trim();
            bool added = false;
            bool firstAfterAdd = false;

            lock (_addressesLock)
            {
                if (!_addresses.Contains(address, StringComparer.Ordinal))
                {
                    _addresses.Add(address);
                    added = true;
                    if (_addresses.Count == 1) // first address overall
                        firstAfterAdd = true;
                }
            }

            if (!added)
                return false;

            Logs.PayServer.LogInformation("Added Nano address to subscription list: {Address}", address);

            // Start polling failsafe for this address
            StartPollingForAddress(address);

            if (firstAfterAdd)
            {
                // Start/restart the WS connection (initial subscribe will send the full snapshot)
                EnsureWebSocketRunningIfNeeded();
            }
            else
            {
                // If already connected, send an update
                _ = TrySendUpdateAsync(accountsAdd: new[] { address }, accountsDel: null);
            }

            return true;
        }

        public bool RemoveAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                return false;

            address = address.Trim();
            bool removed = false;
            bool nowEmpty = false;

            lock (_addressesLock)
            {
                removed = _addresses.Remove(address);
                if (removed && _addresses.Count == 0)
                    nowEmpty = true;
            }

            if (!removed)
                return false;

            Logs.PayServer.LogInformation("Removed Nano address from subscription list: {Address}", address);

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
                _ = TrySendUpdateAsync(accountsAdd: null, accountsDel: new[] { address });
            }

            return true;
        }

        public IReadOnlyCollection<string> GetAddresses()
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
                return;

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
                        Console.WriteLine(message);
                        try
                        {
                            using var doc = JsonDocument.Parse(message);
                            var root = doc.RootElement;
                            if (root.TryGetProperty("message", out var msg) &&
                                msg.TryGetProperty("account", out var account) &&
                                msg.TryGetProperty("hash", out var hash))
                            {
                                Logs.PayServer.LogInformation("WS confirmation: account={Account} hash={Hash}", account.GetString(), hash.GetString());
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

        private async Task SendSubscribeAsync(ClientWebSocket ws, string[] accounts, CancellationToken ct)
        {
            var payload = new
            {
                action = "subscribe",
                topic = "confirmation",
                options = new
                {
                    accounts = accounts
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

        private async Task TrySendUpdateAsync(string[] accountsAdd, string[] accountsDel)
        {
            // Nothing to send
            var addEmpty = accountsAdd == null || accountsAdd.Length == 0;
            var delEmpty = accountsDel == null || accountsDel.Length == 0;
            if (addEmpty && delEmpty)
                return;

            var ws = _currentWebSocket;
            if (ws == null || ws.State != WebSocketState.Open)
                return;

            var payload = new UpdatePayload
            {
                action = "update",
                topic = "confirmation",
                options = new UpdateOptions
                {
                    accounts_add = addEmpty ? null : accountsAdd,
                    accounts_del = delEmpty ? null : accountsDel
                }
            };

            var json = JsonSerializer.Serialize(payload, _jsonOptions);
            var buffer = Encoding.UTF8.GetBytes(json);

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

        private string[] SnapshotAddresses()
        {
            lock (_addressesLock)
            {
                return _addresses.ToArray();
            }
        }

        // Manual polling failsafe

        private void StartPollingForAddress(string address)
        {
            lock (_addressesLock)
            {
                foreach (var kv in _nanoLikeConfiguration.NanoLikeConfigurationItems)
                {
                    var key = (kv.Key, address);
                    if (_pollers.ContainsKey(key))
                        continue;

                    var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_Cts.Token);
                    _pollers[key] = linkedCts;

                    var task = PollConfirmationsLoopAsync(linkedCts.Token, kv.Key, address);
                    _runningTasks.Add(task);
                }
            }
        }

        private void StopPollingForAddress(string address)
        {
            lock (_addressesLock)
            {
                var keysToStop = _pollers.Keys.Where(k => k.Address == address).ToList();
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

        private async Task PollConfirmationsLoopAsync(CancellationToken ct, string cryptoCode, string address)
        {
            var delay = TimeSpan.FromSeconds(5);
            Logs.PayServer.LogInformation("Starting confirmations poll loop for {CryptoCode} address {Address}", cryptoCode, address);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // TODO: Replace with a real check per address
                    // await _NanoRpcProvider.UpdateSummary(cryptoCode).ConfigureAwait(false);
                    Console.WriteLine("SIMULATED POLLING FOR ADDRESS " + address);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logs.PayServer.LogError(ex, "Error polling confirmations for {Address}", address);
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

            Logs.PayServer.LogInformation("Stopped confirmations poll loop for {CryptoCode} address {Address}", cryptoCode, address);
        }

        // Payload helper types for JSON update
        private sealed class UpdatePayload
        {
            public string action { get; set; }
            public string topic { get; set; }
            public UpdateOptions options { get; set; }
        }

        private sealed class UpdateOptions
        {
            public string[] accounts_add { get; set; }
            public string[] accounts_del { get; set; }
        }
    }
}