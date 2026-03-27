using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Tmds.DBus;

namespace CmfBudsService;

/// <summary>
/// Manages a Bluetooth RFCOMM socket connection to the CMF Buds hardware.
///
/// Uses P/Invoke against libc to create a raw AF_BLUETOOTH/BTPROTO_RFCOMM socket.
/// The RFCOMM channel number is discovered automatically by trying channels 1–30
/// and keeping the first that succeeds (ECONNREFUSED = no service on that channel,
/// try next).  The discovered channel is cached per MAC address so subsequent
/// connects are instant.
///
/// Requires an existing ACL Bluetooth link (device must already be BT-connected).
/// When the ACL link is up, ECONNREFUSED responses are very fast (&lt;100 ms per
/// wrong channel), so the full scan completes in well under 10 seconds.
/// </summary>
public sealed class BluetoothService : IDisposable
{
    private const int AfBluetooth   = 31;
    private const int BtprotoRfcomm = 3;

    // Per-process cache: MAC → channel.  Avoids repeated scans after reconnects.
    private static readonly Dictionary<string, byte> _channelCache = new();

    private Socket? _socket;
    // Socket.Connected is always false on P/Invoke-wrapped fds (internal flag never set),
    // so we track connection state explicitly.
    private bool _isConnected;

    public string MacAddress { get; }
    public bool   IsConnected => _isConnected && _socket != null;

    public BluetoothService(string macAddress)
    {
        if (!IsValidMac(macAddress))
            throw new ArgumentException($"Invalid MAC address: '{macAddress}'", nameof(macAddress));
        MacAddress = macAddress.ToUpperInvariant();
    }

    // -----------------------------------------------------------------------
    // Connect
    // -----------------------------------------------------------------------

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (IsConnected) return;

        Console.Error.WriteLine($"[bt] Connecting to {MacAddress}…");

        await ConnectWithScanAsync(MacAddress, ct);

        Console.Error.WriteLine($"[bt] Connected on channel {_channelCache.GetValueOrDefault(MacAddress)}.");
    }

    // -----------------------------------------------------------------------
    // Public I/O (used by CmfBudsServiceImpl's read loop and write path)
    // -----------------------------------------------------------------------

    public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        if (_socket is null) throw NotConnected();
        return _socket.SendAsync(data, SocketFlags.None, ct).AsTask();
    }

    public async Task ReadExactAsync(byte[] buf, int offset, int count, CancellationToken ct)
    {
        if (_socket is null) throw NotConnected();
        int read = 0;
        while (read < count)
        {
            int n = await _socket.ReceiveAsync(
                buf.AsMemory(offset + read, count - read), SocketFlags.None, ct);
            if (n == 0) throw new EndOfStreamException("RFCOMM connection closed by device.");
            read += n;
        }
    }

    public void Disconnect()
    {
        _isConnected = false;
        if (_socket is null) return;
        try { _socket.Shutdown(SocketShutdown.Both); } catch { /* ignore */ }
        _socket.Dispose();
        _socket = null;
    }

    public void Dispose() => Disconnect();

    /// <summary>
    /// Removes the cached channel for this MAC address.
    /// Call this when the device stops responding on the cached channel so the
    /// next connect attempt does a full scan and finds the correct channel.
    /// </summary>
    public static void EvictChannelCache(string mac)
        => _channelCache.Remove(mac.ToUpperInvariant());

    // -----------------------------------------------------------------------
    // Channel scan
    // -----------------------------------------------------------------------

    /// <summary>
    /// The NTAPP (aeac4a03) service is on channel 16 per SDP records.
    /// We try this first, then fall back to a full scan.
    /// </summary>
    private const byte KnownNtappChannel = 16;

    private async Task ConnectWithScanAsync(string mac, CancellationToken ct)
    {
        // 1. Try cached channel
        if (_channelCache.TryGetValue(mac, out byte cached))
        {
            Console.Error.WriteLine($"[bt] Trying cached channel {cached}…");
            int cfd = TryChannel(mac, cached, out bool busy, out bool cacheHostDown);
            if (cfd >= 0) { _isConnected = true; _socket = WrapFd(cfd); return; }
            if (cacheHostDown) throw new BluetoothConnectionException(
                "BT ACL link not established. Waiting for device to connect.");
            if (busy)
            {
                await ResetBluetoothConnectionAsync(mac, ct);
                cfd = TryChannel(mac, cached, out _, out _);
                if (cfd >= 0) { _isConnected = true; _socket = WrapFd(cfd); return; }
            }
            _channelCache.Remove(mac);
        }

        // 2. Try the known NTAPP channel 16 first (from SDP records)
        {
            Console.Error.WriteLine($"[bt] Trying known NTAPP channel {KnownNtappChannel}…");
            int cfd = TryChannel(mac, KnownNtappChannel, out bool busy, out bool ntappHostDown);
            if (cfd >= 0)
            {
                _channelCache[mac] = KnownNtappChannel;
                _isConnected = true;
                _socket = WrapFd(cfd);
                return;
            }
            // EHOSTDOWN: BT ACL link not up yet — stop scanning. The BlueZ watcher will
            // trigger a fresh scan from ch 16 once the device actually connects, preventing
            // us from accidentally connecting to a wrong channel (e.g. HFP on ch 3) when
            // the ACL link comes up mid-scan.
            if (ntappHostDown) throw new BluetoothConnectionException(
                "BT ACL link not established. Waiting for device to connect.");
            if (busy)
            {
                Console.Error.WriteLine($"[bt] Channel {KnownNtappChannel} EBUSY — resetting BT connection…");
                await ResetBluetoothConnectionAsync(mac, ct);
                cfd = TryChannel(mac, KnownNtappChannel, out _, out _);
                if (cfd >= 0)
                {
                    _channelCache[mac] = KnownNtappChannel;
                    _isConnected = true;
                    _socket = WrapFd(cfd);
                    return;
                }
            }
        }

        // 3. Full scan 1–30, collecting EBUSY channels separately
        var busyChannels = new List<byte>();
        for (byte ch = 1; ch <= 30; ch++)
        {
            if (ch == KnownNtappChannel) continue; // already tried
            ct.ThrowIfCancellationRequested();
            Console.Error.WriteLine($"[bt] Trying channel {ch}…");
            int fd = TryChannel(mac, ch, out bool busy, out bool hostDown);
            if (fd >= 0)
            {
                _channelCache[mac] = ch;
                Console.Error.WriteLine($"[bt] Found channel {ch}.");
                _isConnected = true;
                _socket = WrapFd(fd);
                return;
            }
            // EHOSTDOWN mid-scan: ACL link is down. Stop scanning — there is no
            // point continuing since all subsequent channels will also fail.
            if (hostDown) throw new BluetoothConnectionException(
                "BT ACL link not established. Waiting for device to connect.");
            if (busy) busyChannels.Add(ch);
        }

        // 4. If EBUSY channels remain reset BT and retry those
        if (busyChannels.Count > 0)
        {
            Console.Error.WriteLine(
                $"[bt] EBUSY on channels {string.Join(",", busyChannels)} — resetting BT connection…");
            await ResetBluetoothConnectionAsync(mac, ct);
            foreach (byte ch in busyChannels)
            {
                Console.Error.WriteLine($"[bt] Retrying EBUSY channel {ch}…");
                int fd = TryChannel(mac, ch, out _, out _);
                if (fd >= 0)
                {
                    _channelCache[mac] = ch;
                    _isConnected = true;
                    Console.Error.WriteLine($"[bt] Connected on formerly-busy channel {ch}.");
                    _socket = WrapFd(fd);
                    return;
                }
            }
        }

        throw new BluetoothConnectionException(
            "Could not find CMF Buds RFCOMM channel (tried channels 1–30). " +
            "Ensure the device is powered on, in range, and BT-connected.");
    }

    /// <summary>
    /// Disconnects and reconnects the BT device via BlueZ to clear stale RFCOMM DLCs.
    /// </summary>
    private static async Task ResetBluetoothConnectionAsync(string mac, CancellationToken ct)
    {
        string adapterPath = await DeviceDiscovery.FindAdapterPathForDeviceAsync(mac, ct);
        string devPath = adapterPath + "/dev_" + mac.Replace(":", "_");
        try
        {
            using var sysBus = new Connection(Address.System!);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            await sysBus.ConnectAsync().WaitAsync(timeout.Token);
            var device = sysBus.CreateProxy<IDevice1>("org.bluez", devPath);
            Console.Error.WriteLine("[bt] Disconnecting device via BlueZ to clear stale DLCs…");
            await device.DisconnectAsync().WaitAsync(timeout.Token);
            await Task.Delay(2000, ct);
            Console.Error.WriteLine("[bt] Reconnecting device via BlueZ…");
            await device.ConnectAsync().WaitAsync(timeout.Token);
            await Task.Delay(2000, ct);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[bt] BT reset error (continuing): {ex.Message}");
        }
    }

    /// <summary>
    /// Opens a socket and attempts a blocking connect to <paramref name="channel"/>.
    /// Returns the fd on success, –1 on normal failure, or –1 with <paramref name="ebusy"/>=true
    /// when EBUSY (errno=16) indicates a stale local DLC is already holding that channel,
    /// or –1 with <paramref name="hostDown"/>=true when EHOSTDOWN (errno=112) indicates
    /// the BT ACL link is not yet established (device not BT-connected).
    /// </summary>
    private static int TryChannel(string mac, byte channel, out bool ebusy, out bool hostDown)
    {
        ebusy = false;
        hostDown = false;
        int fd = LibcSocket(AfBluetooth, (int)SocketType.Stream, BtprotoRfcomm);
        if (fd < 0) return -1;

        var addr = BuildSockAddrRc(mac, channel);
        int result = LibcConnect(fd, ref addr, Marshal.SizeOf<SockAddrRc>());
        if (result == 0) return fd;  // ← success, caller owns the fd

        int err = Marshal.GetLastWin32Error();
        LibcClose(fd);

        if (err == 16) // EBUSY — stale DLC from a previous connection
        {
            ebusy = true;
            Console.Error.WriteLine($"[bt] Channel {channel}: EBUSY (stale DLC)");
        }
        else if (err == 112) // EHOSTDOWN — BT ACL link not established
        {
            hostDown = true;
            Console.Error.WriteLine($"[bt] Channel {channel} errno={err} (ACL link down)");
        }
        else if (err != 111) // not ECONNREFUSED
        {
            Console.Error.WriteLine($"[bt] Channel {channel} errno={err}");
        }

        return -1;
    }

    private static Socket WrapFd(int fd)
    {
        var handle = new SafeSocketHandle(new IntPtr(fd), ownsHandle: true);
        return new Socket(handle);
    }

    // -----------------------------------------------------------------------
    // P/Invoke
    // -----------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct SockAddrRc
    {
        public ushort sa_family;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public byte[] rc_bdaddr;   // BD_ADDR in reversed byte order
        public byte   rc_channel;
    }

    private static SockAddrRc BuildSockAddrRc(string mac, byte channel)
    {
        string[] parts = mac.Split(':');
        byte[]   addr  = new byte[6];
        for (int i = 0; i < 6; i++)
            addr[5 - i] = Convert.ToByte(parts[i], 16);   // BlueZ: reversed
        return new SockAddrRc { sa_family = AfBluetooth, rc_bdaddr = addr, rc_channel = channel };
    }

    [DllImport("libc", EntryPoint = "socket",  SetLastError = true)]
    private static extern int LibcSocket(int domain, int type, int protocol);

    [DllImport("libc", EntryPoint = "connect", SetLastError = true)]
    private static extern int LibcConnect(int sockfd, ref SockAddrRc addr, int addrlen);

    [DllImport("libc", EntryPoint = "close")]
    private static extern int LibcClose(int fd);

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static InvalidOperationException NotConnected() =>
        new("Not connected. Call ConnectAsync first.");

    private static bool IsValidMac(string mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) return false;
        var parts = mac.Split(':');
        if (parts.Length != 6) return false;
        foreach (var p in parts)
            if (p.Length != 2 || !byte.TryParse(
                    p, System.Globalization.NumberStyles.HexNumber, null, out _))
                return false;
        return true;
    }
}
