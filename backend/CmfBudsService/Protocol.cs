namespace CmfBudsService;

// ---------------------------------------------------------------------------
// ANC modes — byte values sent/received in the SetANC / GetANC commands.
// ---------------------------------------------------------------------------
public enum AncMode : byte
{
    High         = 0x01,  // Active Noise Cancellation — high strength
    Mid          = 0x02,  // ANC — mid strength
    Low          = 0x03,  // ANC — low strength
    Adaptive     = 0x04,  // ANC adaptive
    Off          = 0x05,  // No ANC or transparency
    Transparency = 0x07,  // Transparency / ambient mode
}

/// <summary>Battery state including per-device charging flags.</summary>
public sealed record BatteryState(
    int  Left,  int  Right,  int  Case,
    bool LeftCharging, bool RightCharging, bool CaseCharging);

/// <summary>
/// Builds and parses CMF Buds protocol packets.
///
/// Wire format (every command and response):
///   [0]        0x55       preamble
///   [1]        0x60       fixed
///   [2]        0x01       fixed
///   [3]        cmdLo      command code (uint16 LE) lo byte
///   [4]        cmdHi      command code (uint16 LE) hi byte
///   [5]        payloadLen number of payload bytes (N)
///   [6]        0x00       padding
///   [7]        opId       sequence counter 0–255 (incremented per send)
///   [8..8+N-1] payload    N bytes
///   [8+N]      crcLo      CRC16-MODBUS of bytes [0..8+N-1]
///   [9+N]      crcHi
///
/// Total packet length = 10 + payloadLen bytes.
///
/// Response command code = requestCmd &amp; 0x7FFF  (bit 15 cleared).
/// CRC16: init=0xFFFF, polynomial=0xA001 (Kermit / MODBUS variant).
/// </summary>
public static class Protocol
{
    // -----------------------------------------------------------------------
    // Command codes
    // -----------------------------------------------------------------------
    public const ushort CmdGetBattery       = 49159;
    public const ushort CmdGetANC           = 49182;
    public const ushort CmdSetANC           = 61455;
    public const ushort CmdGetListeningMode = 49232;
    public const ushort CmdSetListeningMode = 61469;
    public const ushort CmdGetAdvancedEQ    = 49228;  // 0xC04C — "is custom EQ active?" flag
    public const ushort CmdSetAdvancedEQ    = 61519;  // 0xF04F — toggle flag (not used for B172/B168)
    public const ushort CmdGetCustomEQ      = 49220;  // 0xC044 — actual custom EQ float values
    public const ushort CmdSetCustomEQ      = 61505;  // 0xF041
    public const ushort CmdGetFirmware      = 49218;
    public const ushort CmdGetInEar         = 49166;
    public const ushort CmdSetInEar         = 61444;
    public const ushort CmdGetLatency       = 49217;
    public const ushort CmdSetLatency       = 61504;
    public const ushort CmdGetGestures      = 49176;
    public const ushort CmdSetGesture       = 61443;
    public const ushort CmdGetUltraBass     = 49230;
    public const ushort CmdSetUltraBass     = 61521;
    public const ushort CmdRingBuds         = 61442;

    // Pre-computed response codes (= cmd & 0x7FFF):
    public static readonly ushort RspGetBattery       = ResponseCmd(CmdGetBattery);
    public static readonly ushort RspGetANC           = ResponseCmd(CmdGetANC);
    public static readonly ushort RspSetANC           = ResponseCmd(CmdSetANC);
    public static readonly ushort RspGetListeningMode = ResponseCmd(CmdGetListeningMode);
    public static readonly ushort RspSetListeningMode = ResponseCmd(CmdSetListeningMode);
    public static readonly ushort RspGetAdvancedEQ    = ResponseCmd(CmdGetAdvancedEQ);
    public static readonly ushort RspSetAdvancedEQ    = ResponseCmd(CmdSetAdvancedEQ);
    public static readonly ushort RspGetCustomEQ      = ResponseCmd(CmdGetCustomEQ);
    public static readonly ushort RspSetCustomEQ      = ResponseCmd(CmdSetCustomEQ);
    public static readonly ushort RspGetFirmware      = ResponseCmd(CmdGetFirmware);
    public static readonly ushort RspGetInEar         = ResponseCmd(CmdGetInEar);
    public static readonly ushort RspSetInEar         = ResponseCmd(CmdSetInEar);
    public static readonly ushort RspGetLatency       = ResponseCmd(CmdGetLatency);
    public static readonly ushort RspSetLatency       = ResponseCmd(CmdSetLatency);
    public static readonly ushort RspGetGestures      = ResponseCmd(CmdGetGestures);
    public static readonly ushort RspSetGesture       = ResponseCmd(CmdSetGesture);
    public static readonly ushort RspGetUltraBass     = ResponseCmd(CmdGetUltraBass);
    public static readonly ushort RspSetUltraBass     = ResponseCmd(CmdSetUltraBass);
    public static readonly ushort RspRingBuds         = ResponseCmd(CmdRingBuds);

    // Device IDs used in battery response and ring/gesture payloads
    public const byte DeviceLeft  = 0x02;
    public const byte DeviceRight = 0x03;
    public const byte DeviceCase  = 0x04;

    private static byte _opId;

    // -----------------------------------------------------------------------
    // Packet builder
    // -----------------------------------------------------------------------

    /// <summary>Builds a fully formed command packet including header and CRC.</summary>
    public static byte[] Build(ushort cmd, byte[]? payload = null)
    {
        payload ??= [];
        int    n     = payload.Length;
        byte   opId  = _opId++;
        byte[] pkt   = new byte[10 + n];

        pkt[0] = 0x55;
        pkt[1] = 0x60;
        pkt[2] = 0x01;
        pkt[3] = (byte)(cmd & 0xFF);
        pkt[4] = (byte)((cmd >> 8) & 0xFF);
        pkt[5] = (byte)n;
        pkt[6] = 0x00;
        pkt[7] = opId;
        payload.CopyTo(pkt, 8);

        ushort crc   = CalcCrc16(pkt.AsSpan(0, 8 + n));
        pkt[8 + n]   = (byte)(crc & 0xFF);
        pkt[9 + n]   = (byte)((crc >> 8) & 0xFF);
        return pkt;
    }

    // -----------------------------------------------------------------------
    // Header accessors
    // -----------------------------------------------------------------------

    /// <summary>Returns true if the 8-byte header starts with the expected preamble.</summary>
    public static bool ValidateHeader(byte[] hdr) =>
        hdr.Length >= 8 && hdr[0] == 0x55 && hdr[1] == 0x60 && hdr[2] == 0x01;

    /// <summary>Extracts the command code from a validated 8-byte header.</summary>
    public static ushort ReadCmd(byte[] hdr) => (ushort)(hdr[3] | (hdr[4] << 8));

    /// <summary>Extracts the payload length from a validated 8-byte header.</summary>
    public static int ReadPayloadLen(byte[] hdr) => hdr[5];

    /// <summary>Validates the CRC of a complete (header + payload + 2-byte CRC) packet.</summary>
    public static bool ValidateCrc(byte[] pkt)
    {
        if (pkt.Length < 10) return false;
        int payloadLen = pkt[5];
        if (pkt.Length < 10 + payloadLen) return false;
        ushort expected = CalcCrc16(pkt.AsSpan(0, 8 + payloadLen));
        ushort actual   = (ushort)(pkt[8 + payloadLen] | (pkt[9 + payloadLen] << 8));
        return expected == actual;
    }

    /// <summary>Returns the expected response command code for a given request command.</summary>
    public static ushort ResponseCmd(ushort requestCmd) => (ushort)(requestCmd & 0x7FFF);

    // -----------------------------------------------------------------------
    // Payload builders (one per command)
    // -----------------------------------------------------------------------

    public static byte[] BuildGetBattery()            => Build(CmdGetBattery);
    public static byte[] BuildGetANC()                => Build(CmdGetANC);
    public static byte[] BuildSetANC(AncMode mode)    => Build(CmdSetANC, [0x01, (byte)mode, 0x00]);
    public static byte[] BuildGetListeningMode()      => Build(CmdGetListeningMode);
    public static byte[] BuildSetListeningMode(byte level) => Build(CmdSetListeningMode, [level, 0x00]);
    public static byte[] BuildGetAdvancedEQ()         => Build(CmdGetAdvancedEQ);
    public static byte[] BuildGetCustomEQ()           => Build(CmdGetCustomEQ);

    /// <summary>
    /// Builds a SetCustomEQ packet using the float payload format from earweb/bluetooth_socket.js.
    /// Band order in wire payload: [mid, treble, bass] at offsets [6, 19, 32] of the 53-byte template.
    /// Values are standard little-endian IEEE 754 floats (valid for -6..+6 dB range).
    /// </summary>
    public static byte[] BuildSetCustomEQ(int bass, int mid, int treble)
    {
        float fBass = bass, fMid = mid, fTreble = treble;
        // Template from earweb setCustomEQ_BT (53 bytes)
        byte[] payload =
        [
            0x03, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x75, 0x44, 0xc3, 0xf5, 0x28, 0x3f, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00,
            0xc0, 0x5a, 0x45, 0x00, 0x00, 0x80, 0x3f, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x0c, 0x43, 0xcd, 0xcc, 0x4c, 0x3f, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00,
        ];
        // highestValue = -(max positive band); [0x00,0x00,0x00,0x80] when all bands ≤ 0
        float maxVal = Math.Max(Math.Max(fBass, fMid), fTreble);
        if (maxVal > 0)
            BitConverter.GetBytes(-maxVal).CopyTo(payload, 1);  // LE float at bytes 1-4
        else
            new byte[] { 0x00, 0x00, 0x00, 0x80 }.CopyTo(payload, 1);
        // band layout (earweb order): band0=mid @6, band1=treble @19, band2=bass @32
        BitConverter.GetBytes(fMid).CopyTo(payload, 6);
        BitConverter.GetBytes(fTreble).CopyTo(payload, 19);
        BitConverter.GetBytes(fBass).CopyTo(payload, 32);
        return Build(CmdSetCustomEQ, payload);
    }
    public static byte[] BuildGetFirmware()           => Build(CmdGetFirmware);
    public static byte[] BuildGetInEar()              => Build(CmdGetInEar);
    public static byte[] BuildSetInEar(bool enabled)  =>
        Build(CmdSetInEar, [0x01, 0x01, enabled ? (byte)1 : (byte)0]);
    public static byte[] BuildGetLatency()            => Build(CmdGetLatency);
    // Latency payload: 0x01=gaming(on), 0x02=normal(off) — matches earweb bluetooth_socket.js.
    public static byte[] BuildSetLatency(bool enabled) =>
        Build(CmdSetLatency, [enabled ? (byte)1 : (byte)2, 0x00]);
    public static byte[] BuildGetUltraBass()          => Build(CmdGetUltraBass);

    /// <param name="displayLevel">Display level 1–5; raw byte sent = displayLevel × 2.</param>
    public static byte[] BuildSetUltraBass(bool enabled, byte displayLevel) =>
        Build(CmdSetUltraBass, [enabled ? (byte)1 : (byte)0, (byte)(displayLevel * 2)]);
    public static byte[] BuildGetGestures()           => Build(CmdGetGestures);
    public static byte[] BuildSetGesture(byte deviceId, byte gestureType, byte action) =>
        Build(CmdSetGesture, [0x01, deviceId, 0x01, gestureType, action]);
    public static byte[] BuildRingBuds(byte deviceId, bool ringing) =>
        Build(CmdRingBuds, [deviceId, ringing ? (byte)1 : (byte)0]);

    // -----------------------------------------------------------------------
    // Response parsers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Parses the battery response.
    /// byte[8] = deviceCount; then pairs [deviceId, battByte] where
    /// bit7 of battByte = charging, bits 0–6 = percentage.
    /// </summary>
    public static BatteryState ParseBattery(byte[] pkt)
    {
        if (pkt.Length < 9) return new(-1, -1, -1, false, false, false);
        int  left  = -1, right = -1, caseL = -1;
        bool lChg  = false, rChg = false, cChg = false;
        int  count = pkt[8];
        for (int i = 0; i < count; i++)
        {
            int off = 9 + i * 2;
            if (off + 1 >= pkt.Length) break;
            byte id  = pkt[off];
            byte raw = pkt[off + 1];
            int  pct = raw & 0x7F;
            bool chg = (raw & 0x80) != 0;
            switch (id)
            {
                case DeviceLeft:  left  = pct; lChg = chg; break;
                case DeviceRight: right = pct; rChg = chg; break;
                case DeviceCase:  caseL = pct; cChg = chg; break;
            }
        }
        return new(left, right, caseL, lChg, rChg, cChg);
    }

    /// <summary>Parses the ANC response — byte[9] is the AncMode value.</summary>
    public static AncMode ParseANC(byte[] pkt) =>
        pkt.Length > 9 ? (AncMode)pkt[9] : AncMode.Off;

    /// <summary>Parses the listening mode response — byte[8] is the preset index (0–6).</summary>
    public static byte ParseListeningMode(byte[] pkt) =>
        pkt.Length > 8 ? pkt[8] : (byte)0;

    /// <summary>
    /// Parses the GetCustomEQ response.
    /// Band data is IEEE 754 LE floats at full-packet offsets 14 (mid), 27 (treble), 40 (bass).
    /// Mirrors the layout used by BuildSetCustomEQ / earweb readCustomEQ.
    /// </summary>
    public static (sbyte Bass, sbyte Mid, sbyte Treble) ParseCustomEQ(byte[] pkt)
    {
        if (pkt.Length < 44) return (0, 0, 0);
        float mid    = BitConverter.ToSingle(pkt, 14);
        float treble = BitConverter.ToSingle(pkt, 27);
        float bass   = BitConverter.ToSingle(pkt, 40);
        return ((sbyte)Math.Round(bass), (sbyte)Math.Round(mid), (sbyte)Math.Round(treble));
    }

    /// <summary>Parses the firmware version string (null-terminated ASCII at byte[8]).</summary>
    public static string ParseFirmware(byte[] pkt)
    {
        if (pkt.Length <= 8) return "";
        int end = 8;
        while (end < pkt.Length && pkt[end] != 0) end++;
        return System.Text.Encoding.ASCII.GetString(pkt, 8, end - 8);
    }

    // InEar GET response payload: [0x01, 0x01, enabled] — enabled is at pkt[10].
    public static bool ParseInEar(byte[] pkt) =>
        pkt.Length > 10 && pkt[10] != 0;

    // Latency GET response payload: [enabled] — 0x01=on, 0x02=off.
    public static bool ParseLatency(byte[] pkt) =>
        pkt.Length > 8 && pkt[8] == 0x01;

    /// <summary>
    /// Parses the ultra bass response.
    /// byte[8]=enabled (0/1), byte[9]=rawLevel — displayLevel = rawLevel / 2 (range 1–5).
    /// </summary>
    public static (bool Enabled, byte Level) ParseUltraBass(byte[] pkt)
    {
        if (pkt.Length < 10) return (false, 1);
        bool    enabled      = pkt[8] != 0;
        byte    raw          = pkt[9];
        byte    displayLevel = raw == 0 ? (byte)1 : (byte)Math.Clamp(raw / 2, 1, 5);
        return (enabled, displayLevel);
    }

    /// <summary>
    /// Parses the gesture list response.
    /// byte[8]=count; then 3-byte entries [deviceId, gestureType, action].
    /// NOTE: the 3-byte-per-entry layout is inferred — verify against a live device response
    /// and adjust if needed (log raw bytes in DispatchNotification to confirm).
    /// </summary>
    /// <summary>
    /// Parses the GetGestures response.
    /// Each entry is 4 bytes: [deviceId, gestureCommon, gestureType, gestureAction].
    /// gestureCommon is skipped; matches earweb readGesture 4-byte stride.
    /// </summary>
    public static List<(byte DeviceId, byte GestureType, byte Action)> ParseGestures(byte[] pkt)
    {
        var result = new List<(byte, byte, byte)>();
        if (pkt.Length < 9) return result;
        int count = pkt[8];
        for (int i = 0; i < count; i++)
        {
            int off = 9 + i * 4;  // 4-byte stride: [deviceId, common, gestureType, action]
            if (off + 3 >= pkt.Length) break;
            result.Add((pkt[off], pkt[off + 2], pkt[off + 3]));  // skip gestureCommon @off+1
        }
        return result;
    }

    // -----------------------------------------------------------------------
    // CRC16-MODBUS
    // -----------------------------------------------------------------------

    public static ushort CalcCrc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
            {
                if ((crc & 1) != 0)
                    crc = (ushort)((crc >> 1) ^ 0xA001);
                else
                    crc >>= 1;
            }
        }
        return crc;
    }
}

