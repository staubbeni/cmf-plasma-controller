using Tmds.DBus;

namespace CmfBudsService;

/// <summary>
/// BlueZ Device1 D-Bus interface (org.bluez on the system bus).
/// Object paths: /org/bluez/hci0/dev_XX_XX_XX_XX_XX_XX
/// Reserved for future use (e.g. triggering device connect at HCI level).
/// </summary>
[DBusInterface("org.bluez.Device1")]
public interface IDevice1 : IDBusObject
{
    Task ConnectAsync();
    Task DisconnectAsync();
    Task ConnectProfileAsync(string uuid);
    Task DisconnectProfileAsync(string uuid);
}
