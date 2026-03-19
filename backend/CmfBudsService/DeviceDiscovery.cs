using System.Diagnostics;
using System.Text.RegularExpressions;

namespace CmfBudsService;

/// <summary>Represents a Bluetooth device that is paired with the local adapter.</summary>
public sealed record PairedDevice(string MacAddress, string Name);

/// <summary>
/// Enumerates Bluetooth devices that are already paired with the system adapter.
///
/// Primary method: BlueZ D-Bus (org.freedesktop.DBus.ObjectManager on the system bus)
/// via bluetoothctl subprocess — avoids an extra runtime dependency while remaining
/// fully compatible with Fedora 40+ BlueZ.
/// </summary>
public static class DeviceDiscovery
{
    private static readonly Regex DeviceLineRegex =
        new(@"^Device\s+(?<mac>(?:[0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2})\s+(?<name>.+)$",
            RegexOptions.Compiled);

    /// <summary>
    /// Returns all paired Bluetooth devices using <c>bluetoothctl paired-devices</c>.
    /// Returns an empty list if BlueZ / bluetoothctl is unavailable.
    /// </summary>
    public static async Task<IReadOnlyList<PairedDevice>> GetPairedDevicesAsync(
        CancellationToken ct = default)
    {
        var devices = new List<PairedDevice>();
        try
        {
            string output = await RunBluetoothctlAsync(ct);
            foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var m = DeviceLineRegex.Match(line.Trim());
                if (m.Success)
                    devices.Add(new PairedDevice(
                        m.Groups["mac"].Value.ToUpperInvariant(),
                        m.Groups["name"].Value.Trim()));
            }
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            // Silently return empty list — the caller will prompt for manual MAC entry.
        }
        return devices;
    }

    /// <summary>
    /// Checks whether the local Bluetooth adapter is powered on.
    /// Uses <c>bluetoothctl show</c> and looks for <c>Powered: yes</c>.
    /// </summary>
    public static async Task<bool> IsAdapterPoweredAsync(CancellationToken ct = default)
    {
        try
        {
            string output = await RunAsync("bluetoothctl", "show", ct);
            return output.Contains("Powered: yes", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Task<string> RunBluetoothctlAsync(CancellationToken ct) =>
        RunAsync("bluetoothctl", "devices Paired", ct);

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
