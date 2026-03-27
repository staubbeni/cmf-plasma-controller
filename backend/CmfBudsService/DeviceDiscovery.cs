using System.Diagnostics;
using System.Text.RegularExpressions;
using Tmds.DBus;

namespace CmfBudsService;

/// <summary>Represents a Bluetooth device that is paired with the local adapter.</summary>
public sealed record PairedDevice(string MacAddress, string Name);

/// <summary>
/// Enumerates Bluetooth devices that are already paired with the system adapter.
///
/// Primary method: BlueZ D-Bus ObjectManager (org.bluez on the system bus) — no
/// subprocess, no regex, works even if bluetoothctl is not in PATH.  Also detects
/// the active HCI adapter dynamically instead of assuming hci0.
///
/// Fallback: <c>bluetoothctl devices Paired</c> subprocess, in case the system bus
/// is inaccessible (e.g. missing D-Bus policy).
/// </summary>
public static class DeviceDiscovery
{
    private static readonly Regex DeviceLineRegex =
        new(@"^Device\s+(?<mac>(?:[0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2})\s+(?<name>.+)$",
            RegexOptions.Compiled);

    /// <summary>
    /// Returns all paired Bluetooth devices.
    /// Tries BlueZ ObjectManager first; falls back to bluetoothctl subprocess.
    /// </summary>
    public static async Task<IReadOnlyList<PairedDevice>> GetPairedDevicesAsync(
        CancellationToken ct = default)
    {
        // Primary: BlueZ ObjectManager via system bus
        try
        {
            // Wrap the entire D-Bus interaction in a timeout so that neither
            // ConnectAsync nor GetManagedObjectsAsync can hang the caller.
            using var bluezTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, bluezTimeout.Token);

            using var conn = new Connection(Address.System!);
            await conn.ConnectAsync().WaitAsync(linked.Token);
            var mgr = conn.CreateProxy<IObjectManager>("org.bluez", "/");
            var objects = await mgr.GetManagedObjectsAsync().WaitAsync(linked.Token);

            var devices = new List<PairedDevice>();
            foreach (var (path, interfaces) in objects)
            {
                if (!interfaces.TryGetValue("org.bluez.Device1", out var props)) continue;
                if (props.TryGetValue("Paired", out var pairedVal) && pairedVal is bool paired && !paired)
                    continue; // not paired

                string mac  = props.TryGetValue("Address", out var a) ? a as string ?? "" : "";
                string name = props.TryGetValue("Name",    out var n) ? n as string ?? "" : mac;
                if (string.IsNullOrEmpty(mac)) continue;

                devices.Add(new PairedDevice(mac.ToUpperInvariant(), name));
            }
            return devices;
        }
        // Only re-throw when the *outer* token is cancelled (service shutdown).
        // A timeout OperationCanceledException should fall through to the bluetoothctl fallback.
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[cmfd] BlueZ ObjectManager unavailable ({ex.Message}), falling back to bluetoothctl");
        }

        // Fallback: bluetoothctl subprocess
        var fallback = new List<PairedDevice>();
        try
        {
            using var btctlTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            btctlTimeout.CancelAfter(TimeSpan.FromSeconds(10));
            string output = await RunAsync("bluetoothctl", "devices Paired", btctlTimeout.Token);
            foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var m = DeviceLineRegex.Match(line.Trim());
                if (m.Success)
                    fallback.Add(new PairedDevice(
                        m.Groups["mac"].Value.ToUpperInvariant(),
                        m.Groups["name"].Value.Trim()));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { /* silently return empty list */ }
        return fallback;
    }

    /// <summary>
    /// Checks whether the local Bluetooth adapter is powered on.
    /// Tries BlueZ ObjectManager first; falls back to bluetoothctl.
    /// </summary>
    public static async Task<bool> IsAdapterPoweredAsync(CancellationToken ct = default)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

            using var conn = new Connection(Address.System!);
            await conn.ConnectAsync().WaitAsync(linked.Token);
            var mgr = conn.CreateProxy<IObjectManager>("org.bluez", "/");
            var objects = await mgr.GetManagedObjectsAsync().WaitAsync(linked.Token);
            foreach (var (_, interfaces) in objects)
            {
                if (!interfaces.TryGetValue("org.bluez.Adapter1", out var props)) continue;
                if (props.TryGetValue("Powered", out var powered) && powered is bool b)
                    return b;
            }
            return false;
        }
        catch (OperationCanceledException) { throw; }
        catch { /* fall through to subprocess */ }

        try
        {
            string output = await RunAsync("bluetoothctl", "show", ct);
            return output.Contains("Powered: yes", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>
    /// Returns the object path of the HCI adapter that has the given device paired,
    /// or the first powered adapter if the device is not found.
    /// Falls back to "/org/bluez/hci0" if the system bus is unavailable.
    /// </summary>
    public static async Task<string> FindAdapterPathForDeviceAsync(
        string macAddress, CancellationToken ct = default)
    {
        const string fallback = "/org/bluez/hci0";
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

            using var conn = new Connection(Address.System!);
            await conn.ConnectAsync().WaitAsync(linked.Token);
            var mgr = conn.CreateProxy<IObjectManager>("org.bluez", "/");
            var objects = await mgr.GetManagedObjectsAsync().WaitAsync(linked.Token);

            string normalised = macAddress.ToUpperInvariant();
            // Walk Device1 objects: /org/bluez/hciN/dev_XX_XX…
            // The parent path segment is the adapter.
            foreach (var (path, interfaces) in objects)
            {
                if (!interfaces.TryGetValue("org.bluez.Device1", out var props)) continue;
                string addr = props.TryGetValue("Address", out var a) ? a as string ?? "" : "";
                if (!addr.Equals(normalised, StringComparison.OrdinalIgnoreCase)) continue;
                // path = /org/bluez/hciN/dev_... → parent = /org/bluez/hciN
                string pathStr = path.ToString();
                int lastSlash = pathStr.LastIndexOf('/');
                return lastSlash > 0 ? pathStr[..lastSlash] : fallback;
            }

            // Device not found — return first powered adapter
            foreach (var (path, interfaces) in objects)
            {
                if (!interfaces.TryGetValue("org.bluez.Adapter1", out var props)) continue;
                if (props.TryGetValue("Powered", out var powered) && powered is true)
                    return path.ToString();
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { /* fall through */ }
        return fallback;
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static async Task<string> RunAsync(string command, string args, CancellationToken ct)
    {
        using var proc = new Process();
        proc.StartInfo = new ProcessStartInfo(command, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
        };
        proc.Start();
        string output = await proc.StandardOutput.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        return output;
    }
}

