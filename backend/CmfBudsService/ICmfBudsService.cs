using Tmds.DBus;

namespace CmfBudsService;

/// <summary>
/// D-Bus interface exposed by the CMF Buds daemon on the session bus.
///
/// Service name : org.kde.cmfbuds
/// Object path  : /org/kde/cmfbuds
/// Interface    : org.kde.cmfbuds
/// </summary>
[DBusInterface("org.kde.cmfbuds")]
public interface ICmfBudsService : IDBusObject
{
    // -----------------------------------------------------------------------
    // ANC / Noise Control
    // -----------------------------------------------------------------------

    /// <summary>
    /// Set the ANC / noise-control mode.
    /// Accepted values: "off" | "transparency" | "anc_high" | "anc_mid" | "anc_low" | "anc_adaptive"
    /// </summary>
    Task SetAncModeAsync(string mode);

    /// <summary>Returns the active ANC mode string (same values as SetAncModeAsync).</summary>
    Task<string> GetCurrentModeAsync();

    // -----------------------------------------------------------------------
    // Battery
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns battery percentages as "left N\nright N\ncase N".
    /// Values are -1 when unknown.
    /// </summary>
    Task<string> GetBatteryLevelsAsync();

    /// <summary>
    /// Returns charging flags as "left N\nright N\ncase N".
    /// Values are 0 (not charging) or 1 (charging).
    /// </summary>
    Task<string> GetChargingStatesAsync();

    // -----------------------------------------------------------------------
    // EQ / Listening mode
    // -----------------------------------------------------------------------

    /// <summary>
    /// Sets the EQ listening mode preset (0–5) or Custom (6).
    /// Preset indices: 0=Dirac OPTEO, 1=Rock, 2=Electronic, 3=Pop, 4=Enhance Vocals, 5=Classical, 6=Custom.
    /// </summary>
    Task SetListeningModeAsync(int level);

    /// <summary>Returns the current listening mode index (0–6).</summary>
    Task<int> GetListeningModeAsync();

    /// <summary>
    /// Sets the custom EQ bands. Each value is in the range –6 to +6.
    /// </summary>
    Task SetCustomEqAsync(int bass, int mid, int treble);

    /// <summary>
    /// Returns the custom EQ values as "bass N\nmid N\ntreble N".
    /// </summary>
    Task<string> GetCustomEqAsync();

    // -----------------------------------------------------------------------
    // Ultra Bass
    // -----------------------------------------------------------------------

    /// <summary>Enables or disables ultra bass enhancement. Level is 1–5.</summary>
    Task SetUltraBassAsync(bool enabled, int level);

    /// <summary>
    /// Returns ultra bass state as "enabled N\nlevel N".
    /// </summary>
    Task<string> GetUltraBassAsync();

    // -----------------------------------------------------------------------
    // In-ear Detection
    // -----------------------------------------------------------------------

    Task SetInEarDetectionAsync(bool enabled);
    Task<bool> GetInEarDetectionAsync();

    // -----------------------------------------------------------------------
    // Low Latency Mode
    // -----------------------------------------------------------------------

    Task SetLowLatencyAsync(bool enabled);
    Task<bool> GetLowLatencyAsync();

    // -----------------------------------------------------------------------
    // Gestures
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns all configured gestures as an array of "side:gestureType:action" strings.
    /// Example: "left:2:9" = left earbud, double pinch, skip forward.
    /// </summary>
    Task<string[]> GetGesturesAsync();

    /// <summary>
    /// Sets a gesture action.  side="left"|"right".
    /// gestureType: 2=double pinch, 3=triple pinch, 7=pinch &amp; hold, 9=double pinch &amp; hold.
    /// action: 1=none, 2=play/pause, 8=skip back, 9=skip forward, 11=voice assistant,
    ///         18=vol up, 19=vol down, 10=NC cycle (all), 20=NC (trans+ANC), 21=NC (ANC+off), 22=NC (trans+off).
    /// </summary>
    Task SetGestureAsync(string side, int gestureType, int action);

    // -----------------------------------------------------------------------
    // Find My Buds
    // -----------------------------------------------------------------------

    /// <summary>Rings or stops ringing one bud. side="left"|"right"; ringing=true starts, false stops.</summary>
    Task RingBudAsync(string side, bool ringing);

    // -----------------------------------------------------------------------
    // Firmware
    // -----------------------------------------------------------------------

    Task<string> GetFirmwareVersionAsync();

    // -----------------------------------------------------------------------
    // Device management
    // -----------------------------------------------------------------------

    /// <summary>Returns all paired Bluetooth devices as "MAC|Name" strings.</summary>
    Task<string[]> GetPairedDevicesAsync();

    /// <summary>Persist the target device MAC address and reconnect.</summary>
    Task SetMacAddressAsync(string macAddress);

    /// <summary>Returns the currently configured MAC address (empty if not set).</summary>
    Task<string> GetMacAddressAsync();

    /// <summary>Returns the current connection state: "disconnected" | "connecting" | "connected" | "error".</summary>
    Task<string> GetConnectionStateAsync();

    // -----------------------------------------------------------------------
    // Signals
    // -----------------------------------------------------------------------

    Task<IDisposable> WatchBatteryUpdatedAsync(
        Action<(int Left, int Right, int Case)> handler,
        Action<Exception>? onError = null);

    Task<IDisposable> WatchModeChangedAsync(
        Action<string> handler,
        Action<Exception>? onError = null);

    Task<IDisposable> WatchConnectionStateChangedAsync(
        Action<string> handler,
        Action<Exception>? onError = null);
}

