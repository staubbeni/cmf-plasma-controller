using Tmds.DBus;

namespace CmfBudsService;

/// <summary>
/// org.freedesktop.DBus.Properties proxy — used to watch PropertiesChanged
/// signals on BlueZ device objects (system bus, org.bluez).
/// </summary>
[DBusInterface("org.freedesktop.DBus.Properties")]
internal interface IProperties : IDBusObject
{
    Task<IDisposable> WatchPropertiesChangedAsync(
        Action<(string InterfaceName, IDictionary<string, object> Changed, string[] Invalidated)> handler,
        Action<Exception>? onError = null);
}

/// <summary>
/// BlueZ Device1 D-Bus interface (org.bluez on the system bus).
/// Object paths: /org/bluez/hci0/dev_XX_XX_XX_XX_XX_XX
/// </summary>
[DBusInterface("org.bluez.Device1")]
public interface IDevice1 : IDBusObject
{
    Task ConnectAsync();
    Task DisconnectAsync();
    Task ConnectProfileAsync(string uuid);
    Task DisconnectProfileAsync(string uuid);
}
