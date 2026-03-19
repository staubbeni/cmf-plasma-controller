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
    // Methods
    // -----------------------------------------------------------------------

    /// <summary>Set the ANC operating mode ("off", "anc", or "transparency").</summary>
    Task SetAncModeAsync(string mode);

    /// <summary>Returns the currently active ANC mode string.</summary>
    Task<string> GetCurrentModeAsync();

    /// <summary>
    /// Returns the latest battery percentages as a dictionary with keys
    /// "left", "right", "case".  Values are -1 when unknown.
    /// </summary>
    Task<IDictionary<string, int>> GetBatteryLevelsAsync();

    /// <summary>Returns all paired Bluetooth devices as "MAC|Name" strings.</summary>
    Task<string[]> GetPairedDevicesAsync();

    /// <summary>Persist the target device MAC address and reconnect.</summary>
    Task SetMacAddressAsync(string macAddress);

    /// <summary>Returns the currently configured MAC address (empty if not set).</summary>
    Task<string> GetMacAddressAsync();

    /// <summary>Returns the current connection state string.</summary>
    Task<string> GetConnectionStateAsync();

    // -----------------------------------------------------------------------
    // Signals
    // -----------------------------------------------------------------------

    /// <summary>Fired when battery levels are refreshed.</summary>
    Task<IDisposable> WatchBatteryUpdatedAsync(
        Action<(int Left, int Right, int Case)> handler,
        Action<Exception>? onError = null);

    /// <summary>Fired when the ANC mode changes.</summary>
    Task<IDisposable> WatchModeChangedAsync(
        Action<string> handler,
        Action<Exception>? onError = null);

    /// <summary>Fired when the Bluetooth connection state changes.</summary>
    Task<IDisposable> WatchConnectionStateChangedAsync(
        Action<string> handler,
        Action<Exception>? onError = null);
}
