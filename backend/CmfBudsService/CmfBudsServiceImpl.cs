using System.Collections.Concurrent;
using Tmds.DBus;

namespace CmfBudsService;

/// <summary>
/// Implements the <see cref="ICmfBudsService"/> D-Bus interface.
///
/// Architecture:
/// - <see cref="BluetoothService"/> owns the RFCOMM socket.
/// - A background <c>ReadLoopAsync</c> continuously reads packets from the socket.
/// - Each outgoing command is tracked by a <c>TaskCompletionSource</c> keyed on the
///   expected response command code; <c>SendAndReceiveAsync</c> awaits this TCS.
/// - Unsolicited notifications (e.g. battery updates pushed by device) are dispatched
///   via <c>DispatchNotification</c>.
/// - Cached state properties are updated after each response and returned immediately
///   for re-entrant callers that haven't yet triggered a connect.
/// </summary>
[DBusInterface("org.kde.cmfbuds")]
public sealed class CmfBudsServiceImpl : ICmfBudsService, IDisposable
{
    public static readonly ObjectPath Path = new("/org/kde/cmfbuds");

    // ── D-Bus signals ───────────────────────────────────────────────────────
    public event Action<(int Left, int Right, int Case)>? OnBatteryUpdated;
    public event Action<string>?                          OnModeChanged;
    public event Action<string>?                          OnConnectionStateChanged;

    // ── Bluetooth state ─────────────────────────────────────────────────────
    private BluetoothService?    _bt;
    private string               _macAddress = string.Empty;
    private string               _connState  = "disconnected";

    // ── Cached device state ─────────────────────────────────────────────────
    private AncMode              _ancMode        = AncMode.Off;
    private BatteryState         _battery        = new(-1, -1, -1, false, false, false);
    private byte                 _listeningMode  = 0;
    private (sbyte Bass, sbyte Mid, sbyte Treble) _customEq = (0, 0, 0);
    private (bool Enabled, byte Level) _ultraBass = (false, 1);
    private bool                 _inEar          = false;
    private bool                 _latency        = false;
    private string               _firmware       = "";
    private List<(byte DeviceId, byte GestureType, byte Action)> _gestures = [];

    // ── Set-guard timestamps (prevent echo-back from overwriting user-set values) ───
    private DateTime _ultraBassSetAt     = DateTime.MinValue;
    private DateTime _listeningModeSetAt = DateTime.MinValue;
    private DateTime _customEqSetAt      = DateTime.MinValue;
    private DateTime _inEarSetAt         = DateTime.MinValue;
    private DateTime _latencySetAt       = DateTime.MinValue;
    private static readonly TimeSpan SetGuard = TimeSpan.FromSeconds(5);

    // ── Async plumbing ──────────────────────────────────────────────────────
    private readonly ConcurrentDictionary<ushort, TaskCompletionSource<byte[]>>
        _pending = new();
    private readonly SemaphoreSlim          _connLock = new(1, 1);
    private readonly CancellationTokenSource _cts     = new();
    private Task?                            _readLoop;
    private Timer?                           _pollTimer;
    private Timer?                           _fullPollTimer;
    private Connection?                      _systemConn;
    private IDisposable?                     _bluezSubscription;
    // Cancelled the moment any path successfully reconnects, aborting the retry loop early.
    private CancellationTokenSource?         _reconnectCts;
    // Tracks which sides have already had a low-battery notification this session.
    private bool                             _lowBattNotifiedLeft;
    private bool                             _lowBattNotifiedRight;

    // ── IDBusObject ─────────────────────────────────────────────────────────
    ObjectPath IDBusObject.ObjectPath => Path;

    // ────────────────────────────────────────────────────────────────────────
    // ANC
    // ────────────────────────────────────────────────────────────────────────

    public async Task SetAncModeAsync(string mode)
    {
        AncMode m = StringToAncMode(mode);
        await SendFireForgetAsync(Protocol.CmdSetANC, [0x01, (byte)m, 0x00]);
        _ancMode = m;
        OnModeChanged?.Invoke(AncModeToString(m));
    }

    public Task<string> GetCurrentModeAsync() =>
        Task.FromResult(AncModeToString(_ancMode));

    // ────────────────────────────────────────────────────────────────────────
    // Battery
    // ────────────────────────────────────────────────────────────────────────

    public Task<string> GetBatteryLevelsAsync() =>
        Task.FromResult($"left {_battery.Left}\nright {_battery.Right}\ncase {_battery.Case}");

    public Task<string> GetChargingStatesAsync() =>
        Task.FromResult($"left {(_battery.LeftCharging ? 1 : 0)}\nright {(_battery.RightCharging ? 1 : 0)}\ncase {(_battery.CaseCharging ? 1 : 0)}");

    // ────────────────────────────────────────────────────────────────────────
    // EQ / Listening mode
    // ────────────────────────────────────────────────────────────────────────

    public async Task SetListeningModeAsync(int level)
    {
        await SendFireForgetAsync(Protocol.CmdSetListeningMode, [(byte)level, 0x00]);
        _listeningMode = (byte)level;
        _listeningModeSetAt = DateTime.UtcNow;
    }

    public Task<int> GetListeningModeAsync() =>
        Task.FromResult((int)_listeningMode);

    public async Task SetCustomEqAsync(int bass, int mid, int treble)
    {
        await EnsureConnectedAsync();
        byte[] packet = Protocol.BuildSetCustomEQ(bass, mid, treble);
        Console.Error.WriteLine($"[cmfd] -> cmd 0x{Protocol.CmdSetCustomEQ:X4} len=53");
        await _bt!.WriteAsync(packet);
        _customEq = ((sbyte)bass, (sbyte)mid, (sbyte)treble);
        _customEqSetAt = DateTime.UtcNow;
        // Ensure the device is in custom EQ mode (level 6)
        if (_listeningMode != 6)
            await SetListeningModeAsync(6);
    }

    public Task<string> GetCustomEqAsync() =>
        Task.FromResult($"bass {_customEq.Bass}\nmid {_customEq.Mid}\ntreble {_customEq.Treble}");

    // ────────────────────────────────────────────────────────────────────────
    // Ultra Bass
    // ────────────────────────────────────────────────────────────────────────

    public async Task SetUltraBassAsync(bool enabled, int level)
    {
        byte l = (byte)Math.Clamp(level, 1, 5);
        await SendFireForgetAsync(Protocol.CmdSetUltraBass,
            [enabled ? (byte)1 : (byte)0, (byte)(l * 2)]);
        _ultraBass = (enabled, l);
        _ultraBassSetAt = DateTime.UtcNow;
    }

    public Task<string> GetUltraBassAsync() =>
        Task.FromResult($"enabled {(_ultraBass.Enabled ? 1 : 0)}\nlevel {_ultraBass.Level}");

    // ────────────────────────────────────────────────────────────────────────
    // In-Ear Detection
    // ────────────────────────────────────────────────────────────────────────

    public async Task SetInEarDetectionAsync(bool enabled)
    {
        await SendFireForgetAsync(Protocol.CmdSetInEar,
            [0x01, 0x01, enabled ? (byte)1 : (byte)0]);
        _inEar = enabled;
        _inEarSetAt = DateTime.UtcNow;
    }

    public Task<bool> GetInEarDetectionAsync() => Task.FromResult(_inEar);

    // ────────────────────────────────────────────────────────────────────────
    // Low Latency
    // ────────────────────────────────────────────────────────────────────────

    public async Task SetLowLatencyAsync(bool enabled)
    {
        await SendFireForgetAsync(Protocol.CmdSetLatency,
            [enabled ? (byte)1 : (byte)2, 0x00]);
        _latency = enabled;
        _latencySetAt = DateTime.UtcNow;
    }

    public Task<bool> GetLowLatencyAsync() => Task.FromResult(_latency);

    // ────────────────────────────────────────────────────────────────────────
    // Gestures
    // ────────────────────────────────────────────────────────────────────────

    public Task<string[]> GetGesturesAsync()
    {
        var result = _gestures.Select(g =>
        {
            string side = g.DeviceId == Protocol.DeviceLeft ? "left" : "right";
            return $"{side}:{g.GestureType}:{g.Action}";
        }).ToArray();
        return Task.FromResult(result);
    }

    public async Task SetGestureAsync(string side, int gestureType, int action)
    {
        byte deviceId = side.Equals("left", StringComparison.OrdinalIgnoreCase)
            ? Protocol.DeviceLeft : Protocol.DeviceRight;
        await SendFireForgetAsync(Protocol.CmdSetGesture,
            [0x01, deviceId, 0x01, (byte)gestureType, (byte)action]);
        // Update cached gesture entry
        var key = (DeviceId: deviceId, GestureType: (byte)gestureType);
        var idx = _gestures.FindIndex(g => g.DeviceId == key.DeviceId && g.GestureType == key.GestureType);
        var entry = (deviceId, (byte)gestureType, (byte)action);
        if (idx >= 0) _gestures[idx] = entry;
        else _gestures.Add(entry);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Find My Buds
    // ────────────────────────────────────────────────────────────────────────

    public async Task RingBudAsync(string side, bool ringing)
    {
        byte deviceId = side.Equals("left", StringComparison.OrdinalIgnoreCase)
            ? Protocol.DeviceLeft : Protocol.DeviceRight;
        await SendFireForgetAsync(Protocol.CmdRingBuds,
            [deviceId, ringing ? (byte)1 : (byte)0]);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Firmware
    // ────────────────────────────────────────────────────────────────────────

    public Task<string> GetFirmwareVersionAsync() => Task.FromResult(_firmware);

    // ────────────────────────────────────────────────────────────────────────
    // Device management
    // ────────────────────────────────────────────────────────────────────────

    public async Task<string[]> GetPairedDevicesAsync()
    {
        var devices = await DeviceDiscovery.GetPairedDevicesAsync(_cts.Token);
        return devices.Select(d => $"{d.MacAddress}|{d.Name}").ToArray();
    }

    public async Task SetMacAddressAsync(string macAddress)
    {
        string normalized = macAddress.ToUpperInvariant();

        await _connLock.WaitAsync(_cts.Token);
        try
        {
            // If the same MAC is already connected, nothing to do.
            if (_macAddress == normalized && _bt?.IsConnected == true)
                return;

            _macAddress = normalized;
            _bt?.Dispose();
            _bt = null;
            SetConnectionState("disconnected");
        }
        finally { _connLock.Release(); }

        _ = Task.Run(async () =>
        {
            try { await EnsureConnectedAsync(_cts.Token); }
            catch (Exception ex) when (!_cts.IsCancellationRequested)
            {
                Console.Error.WriteLine($"[cmfd] Auto-connect after MAC set failed: {ex.Message}");
                // No read loop is running to call HandleDisconnectAsync, so start the
                // retry loop explicitly.  This handles "device already BT-connected but
                // RFCOMM not yet ready" as well as "device not yet reachable at startup".
                _reconnectCts?.Dispose();
                _reconnectCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                _ = Task.Run(() => TryReconnectLoopAsync(_reconnectCts.Token), _cts.Token);
            }
        });

        _ = Task.Run(() => StartBlueZWatcherAsync(_macAddress, _cts.Token));
    }

    public Task<string> GetMacAddressAsync() => Task.FromResult(_macAddress);

    public Task<string> GetConnectionStateAsync() => Task.FromResult(_connState);

    // ────────────────────────────────────────────────────────────────────────
    // D-Bus Signals (Tmds.DBus pattern)
    // ────────────────────────────────────────────────────────────────────────

    public Task<IDisposable> WatchBatteryUpdatedAsync(
        Action<(int Left, int Right, int Case)> handler,
        Action<Exception>? onError = null) =>
        SignalWatcher.AddAsync(this, nameof(OnBatteryUpdated), handler);

    public Task<IDisposable> WatchModeChangedAsync(
        Action<string> handler,
        Action<Exception>? onError = null) =>
        SignalWatcher.AddAsync(this, nameof(OnModeChanged), handler);

    public Task<IDisposable> WatchConnectionStateChangedAsync(
        Action<string> handler,
        Action<Exception>? onError = null) =>
        SignalWatcher.AddAsync(this, nameof(OnConnectionStateChanged), handler);

    // ────────────────────────────────────────────────────────────────────────
    // Polling timer (periodic device state refresh)
    // ────────────────────────────────────────────────────────────────────────

    public void StartPolling()
    {
        // Battery poll: every 30s
        _pollTimer = new Timer(
            async _ =>
            {
                try
                {
                    if (_bt?.IsConnected != true) return;
                    var resp = await SendAndReceiveAsync(Protocol.CmdGetBattery, null, _cts.Token);
                    _battery = Protocol.ParseBattery(resp);
                    OnBatteryUpdated?.Invoke((_battery.Left, _battery.Right, _battery.Case));
                    CheckLowBattery(_battery.Left, _battery.Right);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[cmfd] Battery poll error: {ex.Message}");
                }
            },
            null,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(30));

        // Full state poll: every 30s (offset by 15s from battery)
        // Re-queries the device so phone-side changes are detected.
        _fullPollTimer = new Timer(
            async _ =>
            {
                try
                {
                    if (_bt?.IsConnected != true) return;
                    await RefreshDeviceStateAsync(_cts.Token);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[cmfd] Full poll error: {ex.Message}");
                }
            },
            null,
            TimeSpan.FromSeconds(25),
            TimeSpan.FromSeconds(30));
    }

    /// <summary>Re-queries key state from the device, respecting set-guards.</summary>
    private async Task RefreshDeviceStateAsync(CancellationToken ct)
    {
        async Task TryRefresh(Func<Task> action, string name)
        {
            try { await action(); }
            catch (Exception ex) { Console.Error.WriteLine($"[cmfd] Refresh {name}: {ex.Message}"); }
        }

        await TryRefresh(async () =>
        {
            if (DateTime.UtcNow - _listeningModeSetAt > SetGuard)
            {
                var r = await SendAndReceiveAsync(Protocol.CmdGetListeningMode, ct);
                _listeningMode = Protocol.ParseListeningMode(r);
            }
        }, "listeningMode");

        await TryRefresh(async () =>
        {
            if (DateTime.UtcNow - _ultraBassSetAt > SetGuard)
            {
                var r = await SendAndReceiveAsync(Protocol.CmdGetUltraBass, ct);
                _ultraBass = Protocol.ParseUltraBass(r);
            }
        }, "ultraBass");

        await TryRefresh(async () =>
        {
            if (DateTime.UtcNow - _customEqSetAt > SetGuard)
            {
                var r = await SendAndReceiveAsync(Protocol.CmdGetCustomEQ, ct);
                if (r.Length >= 44)
                    _customEq = Protocol.ParseCustomEQ(r);
            }
        }, "customEq");

        await TryRefresh(async () =>
        {
            var r = await SendAndReceiveAsync(Protocol.CmdGetGestures, ct);
            _gestures = Protocol.ParseGestures(r);
        }, "gestures");

        await TryRefresh(async () =>
        {
            var r = await SendAndReceiveAsync(Protocol.CmdGetANC, ct);
            _ancMode = Protocol.ParseANC(r);
            OnModeChanged?.Invoke(AncModeToString(_ancMode));
        }, "anc");
    }

    // ────────────────────────────────────────────────────────────────────────
    // Internal: connection management
    // ────────────────────────────────────────────────────────────────────────

    private async Task EnsureConnectedAsync(CancellationToken ct = default, bool silent = false)
    {
        if (_bt?.IsConnected == true) return;
        if (string.IsNullOrEmpty(_macAddress))
            throw new InvalidOperationException("No device MAC address configured.");

        await _connLock.WaitAsync(ct);
        try
        {
            if (_bt?.IsConnected == true) return;

            // Wait for previous read loop to exit cleanly (max 5s)
            if (_readLoop is { IsCompleted: false })
            {
                try { await _readLoop.WaitAsync(TimeSpan.FromSeconds(5)); }
                catch { /* timeout — continue */ }
            }
            _readLoop = null;

            _bt?.Dispose();
            _bt = new BluetoothService(_macAddress);
            if (!silent) SetConnectionState("connecting");
            await _bt.ConnectAsync(ct);
            SetConnectionState("connected");
            // Abort any in-flight retry loop — we're already connected.
            _reconnectCts?.CancelAsync();

            // Start background read loop
            _readLoop = Task.Run(() => ReadLoopAsync(_cts.Token), _cts.Token);

            // Fetch all state from the device asynchronously (don't block the caller)
            _ = Task.Run(() => FetchInitialStateAsync(_cts.Token));
        }
        catch
        {
            SetConnectionState("disconnected");
            _bt?.Dispose();
            _bt = null;
            throw;
        }
        finally { _connLock.Release(); }
    }

    // ────────────────────────────────────────────────────────────────────────
    // Internal: background read loop
    // ────────────────────────────────────────────────────────────────────────

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var bt = _bt; // capture to avoid null-ref race with Disconnect
        if (bt is null) return;
        var hdr = new byte[8];
        // Per-read timeout: if the device goes silent for >90s without closing
        // the RFCOMM connection, treat it as a hang and force a reconnect.
        const int ReadTimeoutSecs = 90;
        while (!ct.IsCancellationRequested)
        {
            using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            readTimeout.CancelAfter(TimeSpan.FromSeconds(ReadTimeoutSecs));
            try
            {
                await bt.ReadExactAsync(hdr, 0, 8, readTimeout.Token);

                // Resync: slide one byte at a time until we find the 0x55 preamble.
                while (hdr[0] != 0x55 && !ct.IsCancellationRequested)
                {
                    Array.Copy(hdr, 1, hdr, 0, 7);
                    await bt.ReadExactAsync(hdr, 7, 1, readTimeout.Token);
                }

                if (!Protocol.ValidateHeader(hdr))
                {
                    // 0x55 0x20 0x01 is a known alternate packet format from the
                    // device.  Skip the entire packet body (payloadLen + 2-byte CRC)
                    // in one read instead of re-syncing byte-by-byte.
                    if (hdr[0] == 0x55 && hdr[2] == 0x01 && hdr[5] < 200)
                    {
                        int skip = hdr[5] + 2; // payload + CRC
                        var discard = new byte[skip];
                        await bt.ReadExactAsync(discard, 0, skip, readTimeout.Token);
                    }
                    else
                    {
                        Console.Error.WriteLine(
                            $"[cmfd] Bad header 0x{hdr[0]:X2} 0x{hdr[1]:X2} 0x{hdr[2]:X2} — skipping.");
                    }
                    continue;
                }

                int    payloadLen = Protocol.ReadPayloadLen(hdr);
                byte[] rest       = new byte[payloadLen + 2]; // payload + 2-byte CRC
                await bt.ReadExactAsync(rest, 0, rest.Length, readTimeout.Token);

                byte[] full = new byte[8 + payloadLen + 2];
                hdr.CopyTo(full, 0);
                rest.CopyTo(full, 8);

                if (!Protocol.ValidateCrc(full))
                {
                    Console.Error.WriteLine(
                        $"[cmfd] CRC mismatch for cmd 0x{Protocol.ReadCmd(hdr):X4} — dropping.");
                    continue;
                }

                ushort cmd = Protocol.ReadCmd(hdr);
                Console.Error.WriteLine($"[cmfd] <- cmd 0x{cmd:X4} len={payloadLen}");

                if (_pending.TryRemove(cmd, out var tcs))
                    tcs.TrySetResult(full);
                else
                    DispatchNotification(cmd, full);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Service shutting down — clean exit.
                break;
            }
            catch (OperationCanceledException)
            {
                // readTimeout fired: device has been silent for >90s.
                // Clear the channel cache — the channel we connected on didn't
                // produce any traffic, so it may be the wrong one (e.g. a fallback
                // channel chosen because the correct one was temporarily EBUSY).
                BluetoothService.EvictChannelCache(_macAddress);
                Console.Error.WriteLine("[cmfd] Read timeout — device appears silent, forcing reconnect.");
                break;
            }
            catch (EndOfStreamException)
            {
                Console.Error.WriteLine("[cmfd] Device closed RFCOMM connection.");
                break;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                Console.Error.WriteLine($"[cmfd] Read loop error: {ex.Message}");
                break;
            }
        }

        // Cancel all waiting requests
        foreach (var kv in _pending.ToArray())
            kv.Value.TrySetException(new IOException("Connection closed."));
        _pending.Clear();

        SetConnectionState("disconnected");
        var btOld = _bt;
        _bt = null;
        btOld?.Disconnect();
        try { btOld?.Dispose(); } catch { /* ignore */ }

        // Auto-reconnect unless the service is shutting down
        if (!ct.IsCancellationRequested && !string.IsNullOrEmpty(_macAddress))
        {
            _reconnectCts?.Dispose();
            _reconnectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _ = Task.Run(() => TryReconnectLoopAsync(_reconnectCts.Token), ct);
        }
    }

    private async Task TryReconnectLoopAsync(CancellationToken ct)
    {
        int[] delays = [5, 10, 20, 30, 60];
        int attempt = 0;
        while (!ct.IsCancellationRequested && !string.IsNullOrEmpty(_macAddress))
        {
            // BlueZ watcher may have already reconnected while we were waiting.
            if (_bt?.IsConnected == true) return;

            int secs = delays[Math.Min(attempt, delays.Length - 1)];
            Console.Error.WriteLine($"[cmfd] Reconnect attempt {attempt + 1} in {secs}s…");
            try { await Task.Delay(TimeSpan.FromSeconds(secs), ct); }
            catch (OperationCanceledException) { return; }

            // Re-check: BlueZ watcher may have reconnected during the sleep.
            if (_bt?.IsConnected == true) return;

            try
            {
                await EnsureConnectedAsync(ct, silent: true);
                Console.Error.WriteLine("[cmfd] Reconnected successfully.");
                return;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                Console.Error.WriteLine($"[cmfd] Reconnect failed: {ex.Message}");
                attempt++;
            }
        }
    }

    /// <summary>
    /// Subscribes to BlueZ PropertiesChanged on the system bus so we can detect
    /// when the device reconnects at the Bluetooth level and react immediately,
    /// rather than waiting for the next retry-loop interval.
    /// </summary>
    private async Task StartBlueZWatcherAsync(string mac, CancellationToken ct)
    {
        try
        {
            // Tear down any previous subscription first.
            _bluezSubscription?.Dispose();
            _bluezSubscription = null;

            // Discover the adapter path dynamically (handles hci1, hci2, USB dongles…)
            string adapterPath = await DeviceDiscovery.FindAdapterPathForDeviceAsync(mac, ct);
            string devPath = adapterPath + "/dev_" + mac.Replace(':', '_');

            if (_systemConn == null)
            {
                _systemConn = new Connection(Address.System!);
                await _systemConn.ConnectAsync();
            }

            var proxy = _systemConn.CreateProxy<IProperties>("org.bluez", devPath);
            _bluezSubscription = await proxy.WatchPropertiesChangedAsync(
                args =>
                {
                    if (args.InterfaceName != "org.bluez.Device1") return;
                    if (!args.Changed.TryGetValue("Connected", out var val)) return;
                    if (val is not bool connected || !connected) return;

                    Console.Error.WriteLine("[cmfd] BlueZ: device connected → triggering reconnect");
                    // Cancel the exponential-backoff retry loop — the watcher's
                    // path is faster (2 s settle vs 5 s+ delay).
                    _reconnectCts?.CancelAsync();
                    _ = Task.Run(async () =>
                    {
                        // Settle delay: give PipeWire/PulseAudio time to negotiate the A2DP
                        // audio profile before we open the RFCOMM channel.  300 ms was too
                        // short — the RFCOMM scan (and any EBUSY-triggered BT reset) would
                        // race with A2DP setup, resulting in the device not appearing as an
                        // audio output.
                        try { await Task.Delay(2000, ct); }
                        catch (OperationCanceledException) { return; }
                        try { await EnsureConnectedAsync(ct, silent: true); }
                        catch (Exception ex) when (!ct.IsCancellationRequested)
                        {
                            Console.Error.WriteLine(
                                $"[cmfd] BlueZ-triggered reconnect failed: {ex.Message}");
                        }
                    }, ct);
                },
                ex => Console.Error.WriteLine($"[cmfd] BlueZ watcher error: {ex.Message}"));

            Console.Error.WriteLine($"[cmfd] Watching BlueZ: {devPath}");
            // Note: the 'already connected at startup' case is handled by the
            // TryReconnectLoopAsync started in SetMacAddressAsync on failure.
            // Adding a second immediate EnsureConnectedAsync here races with the
            // SetMacAddressAsync task and causes EALREADY (errno=114) on every
            // RFCOMM channel in both scans, producing a rapid reconnect loop.
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Non-fatal: watcher is a best-effort speed-up; the retry loop still works.
            Console.Error.WriteLine($"[cmfd] BlueZ watcher setup failed (non-fatal): {ex.Message}");
        }
    }

    private void DispatchNotification(ushort cmd, byte[] pkt)
    {
        // Battery (unsolicited push or response to GetBattery)
        if (cmd == Protocol.RspGetBattery)
        {
            _battery = Protocol.ParseBattery(pkt);
            OnBatteryUpdated?.Invoke((_battery.Left, _battery.Right, _battery.Case));
            CheckLowBattery(_battery.Left, _battery.Right);
        }
        // ANC — handle both GetANC and SetANC response codes
        else if (cmd == Protocol.RspGetANC || cmd == Protocol.RspSetANC)
        {
            _ancMode = Protocol.ParseANC(pkt);
            OnModeChanged?.Invoke(AncModeToString(_ancMode));
        }
        // Listening mode (EQ preset) — guard against echo-back within 5s of a SetListeningMode
        else if (cmd == Protocol.RspGetListeningMode || cmd == Protocol.RspSetListeningMode)
        {
            if (DateTime.UtcNow - _listeningModeSetAt > SetGuard)
            {
                _listeningMode = Protocol.ParseListeningMode(pkt);
                Console.Error.WriteLine($"[cmfd] Notif: listeningMode={_listeningMode}");
            }
        }
        // Ultra Bass — guard against echo-back within 5s of a SetUltraBass
        else if (cmd == Protocol.RspGetUltraBass || cmd == Protocol.RspSetUltraBass)
        {
            if (DateTime.UtcNow - _ultraBassSetAt > SetGuard)
            {
                _ultraBass = Protocol.ParseUltraBass(pkt);
                Console.Error.WriteLine($"[cmfd] Notif: ultraBass enabled={_ultraBass.Enabled} level={_ultraBass.Level}");
            }
        }
        // Custom EQ values — guard against echo-back within 5s of a SetCustomEQ
        else if (cmd == Protocol.RspGetCustomEQ || cmd == Protocol.RspSetCustomEQ)
        {
            if (DateTime.UtcNow - _customEqSetAt > SetGuard && pkt.Length >= 44)
                _customEq = Protocol.ParseCustomEQ(pkt);
        }
        // In-ear detection — guard against echo-back
        else if (cmd == Protocol.RspGetInEar || cmd == Protocol.RspSetInEar)
        {
            if (DateTime.UtcNow - _inEarSetAt > SetGuard)
                _inEar = Protocol.ParseInEar(pkt);
        }
        // Low latency — guard against echo-back
        else if (cmd == Protocol.RspGetLatency || cmd == Protocol.RspSetLatency)
        {
            if (DateTime.UtcNow - _latencySetAt > SetGuard)
                _latency = Protocol.ParseLatency(pkt);
        }
        // Firmware (may arrive after reconnect)
        else if (cmd == Protocol.RspGetFirmware)
        {
            _firmware = Protocol.ParseFirmware(pkt);
        }
        else
        {
            Console.Error.WriteLine($"[cmfd] Unhandled notification cmd 0x{cmd:X4}");
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // Internal: SendAndReceive + initial state fetch
    // ────────────────────────────────────────────────────────────────────────

    private async Task<byte[]> SendAndReceiveAsync(
        ushort cmd, byte[]? payload, CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);

        var     tcs         = new TaskCompletionSource<byte[]>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        ushort  responseCmd = Protocol.ResponseCmd(cmd);
        _pending[responseCmd] = tcs;

        byte[] packet = Protocol.Build(cmd, payload);
        Console.Error.WriteLine($"[cmfd] -> cmd 0x{cmd:X4} len={payload?.Length ?? 0}");
        await _bt!.WriteAsync(packet, ct);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linked  = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            linked.Token.Register(() => tcs.TrySetCanceled());
            return await tcs.Task;
        }
        finally
        {
            _pending.TryRemove(responseCmd, out _);
        }
    }

    // Overload without explicit payload (empty payload)
    private Task<byte[]> SendAndReceiveAsync(ushort cmd, CancellationToken ct = default) =>
        SendAndReceiveAsync(cmd, null, ct);

    // Fire-and-forget: sends a command without waiting for a response.
    // Used for SET commands where the device echoes back using the GET response code,
    // which is handled by DispatchNotification after it arrives.
    private async Task SendFireForgetAsync(ushort cmd, byte[]? payload, CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);
        byte[] packet = Protocol.Build(cmd, payload);
        Console.Error.WriteLine($"[cmfd] -> cmd 0x{cmd:X4} len={payload?.Length ?? 0}");
        await _bt!.WriteAsync(packet, ct);
    }

    private async Task FetchInitialStateAsync(CancellationToken ct)
    {
        async Task TryFetch(Func<Task> action, string name)
        {
            try { await action(); }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            { Console.Error.WriteLine($"[cmfd] Init fetch {name}: {ex.Message}"); }
        }

        await TryFetch(async () =>
        {
            var r = await SendAndReceiveAsync(Protocol.CmdGetBattery, ct);
            _battery = Protocol.ParseBattery(r);
            OnBatteryUpdated?.Invoke((_battery.Left, _battery.Right, _battery.Case));
            CheckLowBattery(_battery.Left, _battery.Right);
        }, "battery");

        await TryFetch(async () =>
        {
            var r = await SendAndReceiveAsync(Protocol.CmdGetANC, ct);
            _ancMode = Protocol.ParseANC(r);
            OnModeChanged?.Invoke(AncModeToString(_ancMode));
        }, "anc");

        await TryFetch(async () =>
        {
            var r = await SendAndReceiveAsync(Protocol.CmdGetListeningMode, ct);
            _listeningMode = Protocol.ParseListeningMode(r);
        }, "listeningMode");

        await TryFetch(async () =>
        {
            var r = await SendAndReceiveAsync(Protocol.CmdGetCustomEQ, ct);
            _customEq = Protocol.ParseCustomEQ(r);
        }, "customEq");

        await TryFetch(async () =>
        {
            var r = await SendAndReceiveAsync(Protocol.CmdGetFirmware, ct);
            _firmware = Protocol.ParseFirmware(r);
            Console.Error.WriteLine($"[cmfd] Firmware: {_firmware}");
        }, "firmware");

        await TryFetch(async () =>
        {
            var r = await SendAndReceiveAsync(Protocol.CmdGetInEar, ct);
            _inEar = Protocol.ParseInEar(r);
        }, "inEar");

        await TryFetch(async () =>
        {
            var r = await SendAndReceiveAsync(Protocol.CmdGetLatency, ct);
            _latency = Protocol.ParseLatency(r);
        }, "latency");

        await TryFetch(async () =>
        {
            var r = await SendAndReceiveAsync(Protocol.CmdGetUltraBass, ct);
            _ultraBass = Protocol.ParseUltraBass(r);
        }, "ultraBass");

        await TryFetch(async () =>
        {
            var r = await SendAndReceiveAsync(Protocol.CmdGetGestures, ct);
            _gestures = Protocol.ParseGestures(r);
        }, "gestures");
    }

    private void SetConnectionState(string state)
    {
        if (_connState == state) return;
        _connState = state;
        OnConnectionStateChanged?.Invoke(_connState);
        Console.Error.WriteLine($"[cmfd] Connection state: {state}");
        // Reset low-battery notification flags on each new connection.
        if (state == "connected")
        {
            _lowBattNotifiedLeft  = false;
            _lowBattNotifiedRight = false;
        }
    }

    private const int LowBatteryThreshold = 20;

    /// <summary>
    /// Fires a desktop notification for a bud whose battery dropped below
    /// <see cref="LowBatteryThreshold"/>%. Each side notifies at most once per
    /// connection session to avoid repeated alerts.
    /// </summary>
    private void CheckLowBattery(int left, int right)
    {
        if (left  >= 0 && left  <= LowBatteryThreshold && !_lowBattNotifiedLeft)
        {
            _lowBattNotifiedLeft = true;
            _ = Task.Run(() => NotifySendAsync($"Left bud at {left}%", "CMF Buds: Low battery"));
        }
        if (right >= 0 && right <= LowBatteryThreshold && !_lowBattNotifiedRight)
        {
            _lowBattNotifiedRight = true;
            _ = Task.Run(() => NotifySendAsync($"Right bud at {right}%", "CMF Buds: Low battery"));
        }
    }

    private static async Task NotifySendAsync(string body, string summary)
    {
        try
        {
            using var proc = new System.Diagnostics.Process();
            proc.StartInfo = new System.Diagnostics.ProcessStartInfo(
                "notify-send",
                $"--urgency=normal --expire-time=8000 --app-name=\"CMF Buds\" \"{summary}\" \"{body}\"")
            {
                UseShellExecute        = false,
                RedirectStandardOutput = false,
                RedirectStandardError  = false,
            };
            proc.Start();
            await proc.WaitForExitAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[cmfd] notify-send failed: {ex.Message}");
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // ANC mode string helpers
    // ────────────────────────────────────────────────────────────────────────

    private static AncMode StringToAncMode(string s) => s.ToLowerInvariant() switch
    {
        "anc_high"     or "anc"  => AncMode.High,
        "anc_mid"                => AncMode.Mid,
        "anc_low"                => AncMode.Low,
        "anc_adaptive"           => AncMode.Adaptive,
        "transparency"           => AncMode.Transparency,
        _                        => AncMode.Off,
    };

    private static string AncModeToString(AncMode m) => m switch
    {
        AncMode.High         => "anc_high",
        AncMode.Mid          => "anc_mid",
        AncMode.Low          => "anc_low",
        AncMode.Adaptive     => "anc_adaptive",
        AncMode.Transparency => "transparency",
        _                    => "off",
    };

    // ────────────────────────────────────────────────────────────────────────
    // IDisposable
    // ────────────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _cts.Cancel();
        _reconnectCts?.Dispose();
        _bluezSubscription?.Dispose();
        try { _systemConn?.Dispose(); } catch { }
        _pollTimer?.Dispose();
        _fullPollTimer?.Dispose();
        _bt?.Dispose();
        _connLock.Dispose();
        _cts.Dispose();
    }
}

