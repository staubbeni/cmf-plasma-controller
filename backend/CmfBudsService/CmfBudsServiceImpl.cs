using Tmds.DBus;

namespace CmfBudsService;

/// <summary>
/// Implements the <see cref="ICmfBudsService"/> D-Bus interface.
/// Owns the <see cref="BluetoothService"/> RFCOMM socket and runs a
/// background timer that polls the device for battery levels every 60 seconds.
/// </summary>
[DBusInterface("org.kde.cmfbuds")]
public sealed class CmfBudsServiceImpl : ICmfBudsService, IDisposable
{
    public static readonly ObjectPath Path = new("/org/kde/cmfbuds");

    // Battery poll interval
    private const int BatteryPollIntervalSeconds = 60;

    // D-Bus signal events
    public event Action<(int Left, int Right, int Case)>? OnBatteryUpdated;
    public event Action<string>?                          OnModeChanged;
    public event Action<string>?                          OnConnectionStateChanged;

    private BluetoothService?         _bt;
    private string                    _macAddress  = string.Empty;
    private string                    _currentMode = "off";
    private string                    _connState   = "disconnected";
    private (int Left, int Right, int Case) _battery = (-1, -1, -1);

    private readonly SemaphoreSlim    _lock        = new(1, 1);
    private Timer?                    _pollTimer;
    private readonly CancellationTokenSource _cts = new();

    // -----------------------------------------------------------------------
    // IDBusObject
    // -----------------------------------------------------------------------

    ObjectPath IDBusObject.ObjectPath => Path;

    // -----------------------------------------------------------------------
    // ICmfBudsService – Methods
    // -----------------------------------------------------------------------

    public async Task SetAncModeAsync(string mode)
    {
        AncMode ancMode = mode.ToLowerInvariant() switch
        {
            "anc"          => AncMode.ANC,
            "transparency" => AncMode.Transparency,
            _              => AncMode.Off,
        };

        await EnsureConnectedAsync();

        byte[] packet = Protocol.BuildAncPacket(ancMode);
        await _bt!.SendAsync(packet, _cts.Token);

        _currentMode = mode.ToLowerInvariant();
        OnModeChanged?.Invoke(_currentMode);
    }

    public Task<string> GetCurrentModeAsync() => Task.FromResult(_currentMode);

    public Task<IDictionary<string, int>> GetBatteryLevelsAsync()
    {
        IDictionary<string, int> result = new Dictionary<string, int>
        {
            ["left"]  = _battery.Left,
            ["right"] = _battery.Right,
            ["case"]  = _battery.Case,
        };
        return Task.FromResult(result);
    }

    public async Task<string[]> GetPairedDevicesAsync()
    {
        var devices = await DeviceDiscovery.GetPairedDevicesAsync(_cts.Token);
        return devices.Select(d => $"{d.MacAddress}|{d.Name}").ToArray();
    }

    public async Task SetMacAddressAsync(string macAddress)
    {
        await _lock.WaitAsync(_cts.Token);
        try
        {
            _macAddress = macAddress.ToUpperInvariant();
            _bt?.Dispose();
            _bt = null;
            SetConnectionState("disconnected");
        }
        finally { _lock.Release(); }

        await EnsureConnectedAsync();
    }

    public Task<string> GetMacAddressAsync() => Task.FromResult(_macAddress);

    public Task<string> GetConnectionStateAsync() => Task.FromResult(_connState);

    // -----------------------------------------------------------------------
    // ICmfBudsService – Signals (Tmds.DBus pattern)
    // -----------------------------------------------------------------------

    public Task<IDisposable> WatchBatteryUpdatedAsync(
        Action<(int Left, int Right, int Case)> handler,
        Action<Exception>? onError = null)
    {
        return SignalWatcher.AddAsync(this, nameof(OnBatteryUpdated), handler);
    }

    public Task<IDisposable> WatchModeChangedAsync(
        Action<string> handler,
        Action<Exception>? onError = null)
    {
        return SignalWatcher.AddAsync(this, nameof(OnModeChanged), handler);
    }

    public Task<IDisposable> WatchConnectionStateChangedAsync(
        Action<string> handler,
        Action<Exception>? onError = null)
    {
        return SignalWatcher.AddAsync(this, nameof(OnConnectionStateChanged), handler);
    }

    // -----------------------------------------------------------------------
    // Battery polling
    // -----------------------------------------------------------------------

    /// <summary>Starts the background battery polling timer.</summary>
    public void StartPolling()
    {
        _pollTimer = new Timer(
            async _ => await PollBatteryAsync(),
            null,
            TimeSpan.FromSeconds(5),   // initial delay after start
            TimeSpan.FromSeconds(BatteryPollIntervalSeconds));
    }

    private async Task PollBatteryAsync()
    {
        try
        {
            await EnsureConnectedAsync();
            byte[] request = Protocol.BuildBatteryRequestPacket();
            await _bt!.SendAsync(request, _cts.Token);
            byte[] response = await _bt.ReceiveAsync(32, _cts.Token);
            var levels = Protocol.ParseBatteryResponse(response);
            if (levels.Left >= 0)
            {
                _battery = levels;
                OnBatteryUpdated?.Invoke(_battery);
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[cmfd] Battery poll error: {ex.Message}");
            SetConnectionState("error");
            _bt?.Dispose();
            _bt = null;
        }
    }

    // -----------------------------------------------------------------------
    // Connection management
    // -----------------------------------------------------------------------

    private async Task EnsureConnectedAsync()
    {
        if (_bt?.IsConnected == true) return;

        if (string.IsNullOrEmpty(_macAddress))
            throw new InvalidOperationException("No device MAC address configured.");

        if (!await DeviceDiscovery.IsAdapterPoweredAsync(_cts.Token))
            throw new InvalidOperationException("Bluetooth adapter is powered off.");

        await _lock.WaitAsync(_cts.Token);
        try
        {
            // Double-check inside lock
            if (_bt?.IsConnected == true) return;

            _bt?.Dispose();
            _bt = new BluetoothService(_macAddress);
            SetConnectionState("connecting");
            await _bt.ConnectAsync(_cts.Token);
            SetConnectionState("connected");
        }
        catch
        {
            SetConnectionState("disconnected");
            _bt?.Dispose();
            _bt = null;
            throw;
        }
        finally { _lock.Release(); }
    }

    private void SetConnectionState(string state)
    {
        if (_connState == state) return;
        _connState = state;
        OnConnectionStateChanged?.Invoke(_connState);
    }

    // -----------------------------------------------------------------------
    // IDisposable
    // -----------------------------------------------------------------------

    public void Dispose()
    {
        _cts.Cancel();
        _pollTimer?.Dispose();
        _bt?.Dispose();
        _lock.Dispose();
        _cts.Dispose();
    }
}
