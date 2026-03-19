using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace CmfBudsService;

/// <summary>
/// Manages a Bluetooth RFCOMM socket connection to the CMF Buds hardware.
///
/// Uses P/Invoke against libc to connect a raw AF_BLUETOOTH/BTPROTO_RFCOMM socket
/// because the .NET System.Net.Sockets.Socket class does not expose a
/// sockaddr_rc-aware Connect overload.
/// </summary>
public sealed class BluetoothService : IDisposable
{
    // Linux socket constants
    private const int AfBluetooth  = 31;
    private const int BtprotoRfcomm = 3;
    private const byte RfcommChannel = 15; // CMF Buds Pro 2 RFCOMM channel

    private Socket? _socket;
    private bool    _connected;

    public string MacAddress { get; }
    public bool   IsConnected => _connected && _socket != null;

    public BluetoothService(string macAddress)
    {
        if (!IsValidMac(macAddress))
            throw new ArgumentException($"Invalid MAC address format: '{macAddress}'", nameof(macAddress));
        MacAddress = macAddress.ToUpperInvariant();
    }

    /// <summary>Opens the RFCOMM socket and connects to the device.</summary>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_connected) return;

        _socket = new Socket(
            (AddressFamily)AfBluetooth,
            SocketType.Stream,
            (ProtocolType)BtprotoRfcomm);

        var addr = BuildSockAddrRc(MacAddress, RfcommChannel);
        await Task.Run(() => ConnectRfcomm(_socket, ref addr), ct);
        _connected = true;
    }

    /// <summary>Sends a raw byte packet to the device.</summary>
    public async Task SendAsync(byte[] data, CancellationToken ct = default)
    {
        EnsureConnected();
        await _socket!.SendAsync(data.AsMemory(), SocketFlags.None, ct);
    }

    /// <summary>Receives a raw byte response from the device.</summary>
    public async Task<byte[]> ReceiveAsync(int bufferSize = 32, CancellationToken ct = default)
    {
        EnsureConnected();
        var buffer = new byte[bufferSize];
        int received = await _socket!.ReceiveAsync(buffer.AsMemory(), SocketFlags.None, ct);
        return buffer[..received];
    }

    public void Disconnect()
    {
        if (_socket is null) return;
        try { _socket.Shutdown(SocketShutdown.Both); } catch { /* ignore */ }
        _socket.Close();
        _connected = false;
    }

    public void Dispose()
    {
        Disconnect();
        _socket?.Dispose();
    }

    // -----------------------------------------------------------------------
    // P/Invoke helpers
    // -----------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct SockAddrRc
    {
        public ushort sa_family;          // AF_BLUETOOTH = 31
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public byte[] rc_bdaddr;          // BD_ADDR in little-endian
        public byte   rc_channel;
    }

    private static SockAddrRc BuildSockAddrRc(string mac, byte channel)
    {
        string[] parts = mac.Split(':');
        byte[]   addr  = new byte[6];
        // BlueZ stores BD_ADDR in reversed byte order
        for (int i = 0; i < 6; i++)
            addr[5 - i] = Convert.ToByte(parts[i], 16);

        return new SockAddrRc
        {
            sa_family  = AfBluetooth,
            rc_bdaddr  = addr,
            rc_channel = channel,
        };
    }

    [DllImport("libc", EntryPoint = "connect", SetLastError = true)]
    private static extern int LibcConnect(int sockfd, ref SockAddrRc addr, int addrlen);

    private static void ConnectRfcomm(Socket socket, ref SockAddrRc addr)
    {
        int fd     = (int)socket.Handle;
        int addrLen = Marshal.SizeOf<SockAddrRc>();
        int result = LibcConnect(fd, ref addr, addrLen);
        if (result == 0) return;

        int errno = Marshal.GetLastWin32Error();
        throw errno switch
        {
            2  => new BluetoothConnectionException(
                      "Bluetooth device not found. Ensure the device is paired, powered on, and in range."),
            16 => new BluetoothConnectionException(
                      "Socket busy. Another application is already connected to the device."),
            112 => new BluetoothConnectionException(
                      "Host is down. The device may be out of range or powered off."),
            _  => new SocketException(errno),
        };
    }

    private void EnsureConnected()
    {
        if (!IsConnected)
            throw new InvalidOperationException("Not connected. Call ConnectAsync first.");
    }

    private static bool IsValidMac(string mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) return false;
        string[] parts = mac.Split(':');
        if (parts.Length != 6) return false;
        foreach (var p in parts)
            if (p.Length != 2 || !byte.TryParse(p, System.Globalization.NumberStyles.HexNumber, null, out _))
                return false;
        return true;
    }
}
