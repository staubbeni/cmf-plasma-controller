namespace CmfBudsService;

/// <summary>
/// ANC operating modes supported by CMF Buds hardware.
/// </summary>
public enum AncMode : byte
{
    Off          = 0x00,
    ANC          = 0x01,
    Transparency = 0x02,
}

/// <summary>
/// Builds and parses packets for the CMF / Nothing "Dante" Bluetooth protocol.
///
/// Wire format (command packet):
///   [0] 0x55  – Preamble
///   [1] 0x60  – Command byte (ANC) | 0x61 (battery poll)
///   [2] Mode  – 0x00 Off / 0x01 ANC / 0x02 Transparency  (ANC packet only)
///   [n] XOR checksum of all preceding bytes
///
/// Battery response format (from device):
///   [0] 0x55  – Preamble
///   [1] 0x61  – Battery command echo
///   [2] Left  – percentage 0-100
///   [3] Right – percentage 0-100
///   [4] Case  – percentage 0-100
///   [5] XOR checksum
/// </summary>
public static class Protocol
{
    private const byte Preamble    = 0x55;
    private const byte CmdAnc      = 0x60;
    private const byte CmdBattery  = 0x61;

    /// <summary>Builds the 4-byte ANC mode command packet.</summary>
    public static byte[] BuildAncPacket(AncMode mode)
    {
        byte[] packet = [Preamble, CmdAnc, (byte)mode, 0x00];
        packet[^1] = Checksum(packet[..^1]);
        return packet;
    }

    /// <summary>Builds the 3-byte battery poll request packet.</summary>
    public static byte[] BuildBatteryRequestPacket()
    {
        byte[] packet = [Preamble, CmdBattery, 0x00];
        packet[^1] = Checksum(packet[..^1]);
        return packet;
    }

    /// <summary>
    /// Parses a battery response packet.
    /// Returns (left, right, caseLevel); values are -1 if the packet is malformed.
    /// </summary>
    public static (int Left, int Right, int Case) ParseBatteryResponse(byte[] data)
    {
        if (data is null || data.Length < 6) return (-1, -1, -1);
        if (data[0] != Preamble || data[1] != CmdBattery) return (-1, -1, -1);

        byte expected = Checksum(data[..^1]);
        if (data[^1] != expected) return (-1, -1, -1);

        return (data[2], data[3], data[4]);
    }

    /// <summary>Verifies the checksum byte at the end of any received packet.</summary>
    public static bool VerifyChecksum(byte[] packet)
    {
        if (packet is null || packet.Length < 2) return false;
        return Checksum(packet[..^1]) == packet[^1];
    }

    private static byte Checksum(ReadOnlySpan<byte> bytes)
    {
        byte result = 0;
        foreach (byte b in bytes)
            result ^= b;
        return result;
    }
}
