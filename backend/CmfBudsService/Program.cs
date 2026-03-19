using Tmds.DBus;
using CmfBudsService;

// ---------------------------------------------------------------------------
// cmfd — CMF Buds D-Bus daemon
// ---------------------------------------------------------------------------
// Usage:
//   cmfd [--mac XX:XX:XX:XX:XX:XX] [--list-devices]
//
// The daemon registers on the session bus as org.kde.cmfbuds and exposes
// the /org/kde/cmfbuds object.  It is typically auto-started by the Plasma
// widget via D-Bus service activation (org.kde.cmfbuds.service).
// ---------------------------------------------------------------------------

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine("cmfd — CMF Buds Plasma controller daemon");
    Console.WriteLine();
    Console.WriteLine("Usage: cmfd [options]");
    Console.WriteLine("  --mac <XX:XX:XX:XX:XX:XX>   Target device MAC address");
    Console.WriteLine("  --list-devices               Print paired Bluetooth devices and exit");
    Console.WriteLine("  --help                       Show this help");
    return 0;
}

// --list-devices: enumerate paired devices and exit
if (args.Contains("--list-devices"))
{
    bool powered = await DeviceDiscovery.IsAdapterPoweredAsync();
    if (!powered)
    {
        Console.Error.WriteLine("ERROR: Bluetooth adapter is powered off.");
        return 2;
    }
    var devices = await DeviceDiscovery.GetPairedDevicesAsync();
    if (devices.Count == 0)
    {
        Console.WriteLine("No paired Bluetooth devices found.");
        return 0;
    }
    foreach (var d in devices)
        Console.WriteLine($"{d.MacAddress}  {d.Name}");
    return 0;
}

// Parse optional --mac argument
string? initialMac = null;
for (int i = 0; i < args.Length - 1; i++)
    if (args[i] == "--mac") { initialMac = args[i + 1]; break; }

// ---------------------------------------------------------------------------
// Register on the session D-Bus
// ---------------------------------------------------------------------------
using var service = new CmfBudsServiceImpl();

var conn = new Connection(Address.Session!);
await conn.ConnectAsync();
await conn.RegisterServiceAsync("org.kde.cmfbuds");
await conn.RegisterObjectAsync(service);

if (!string.IsNullOrEmpty(initialMac))
    await service.SetMacAddressAsync(initialMac);

service.StartPolling();

Console.WriteLine("[cmfd] Running on org.kde.cmfbuds — press Ctrl+C to stop.");

// Keep the process alive until signalled
var tcs = new TaskCompletionSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; tcs.TrySetResult(); };
AppDomain.CurrentDomain.ProcessExit += (_, _) => tcs.TrySetResult();

await tcs.Task;
return 0;
