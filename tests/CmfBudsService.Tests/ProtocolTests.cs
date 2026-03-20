using CmfBudsService;
using Xunit;

namespace CmfBudsService.Tests;

/// <summary>
/// Unit tests for <see cref="Protocol"/> — verifies packet building, CRC,
/// header validation, and response parsing against the actual wire format.
/// </summary>
public class ProtocolTests
{
    // -----------------------------------------------------------------------
    // Build() — header / CRC structure
    // -----------------------------------------------------------------------

    [Fact]
    public void Build_EmptyPayload_HasCorrectHeaderAndLength()
    {
        byte[] pkt = Protocol.Build(Protocol.CmdGetBattery);

        Assert.Equal(10, pkt.Length); // 8-byte header + 0-byte payload + 2-byte CRC
        Assert.Equal(0x55, pkt[0]);   // preamble
        Assert.Equal(0x60, pkt[1]);   // fixed
        Assert.Equal(0x01, pkt[2]);   // fixed
        Assert.Equal(0, pkt[5]);      // payload length
    }

    [Fact]
    public void Build_WithPayload_EmbedsCmdAndLength()
    {
        byte[] payload = [0x01, 0x02, 0x03];
        byte[] pkt = Protocol.Build(Protocol.CmdSetANC, payload);

        Assert.Equal(13, pkt.Length); // 8 + 3 + 2
        Assert.Equal(3, pkt[5]);      // payload length
        Assert.Equal(0x01, pkt[8]);   // first payload byte
        Assert.Equal(0x02, pkt[9]);
        Assert.Equal(0x03, pkt[10]);
    }

    [Fact]
    public void Build_CrcIsValid()
    {
        byte[] pkt = Protocol.Build(Protocol.CmdGetBattery);
        Assert.True(Protocol.ValidateCrc(pkt));
    }

    [Fact]
    public void Build_SetANC_CrcIsValid()
    {
        byte[] pkt = Protocol.BuildSetANC(AncMode.High);
        Assert.True(Protocol.ValidateCrc(pkt));
    }

    // -----------------------------------------------------------------------
    // ValidateHeader
    // -----------------------------------------------------------------------

    [Fact]
    public void ValidateHeader_CorrectPreamble_ReturnsTrue()
    {
        byte[] hdr = [0x55, 0x60, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00];
        Assert.True(Protocol.ValidateHeader(hdr));
    }

    [Fact]
    public void ValidateHeader_WrongPreamble_ReturnsFalse()
    {
        byte[] hdr = [0xAA, 0x60, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00];
        Assert.False(Protocol.ValidateHeader(hdr));
    }

    [Fact]
    public void ValidateHeader_TooShort_ReturnsFalse()
    {
        Assert.False(Protocol.ValidateHeader([0x55, 0x60]));
    }

    // -----------------------------------------------------------------------
    // ValidateCrc
    // -----------------------------------------------------------------------

    [Fact]
    public void ValidateCrc_TamperedByte_ReturnsFalse()
    {
        byte[] pkt = Protocol.BuildGetBattery();
        pkt[3] ^= 0xFF; // corrupt cmd byte
        Assert.False(Protocol.ValidateCrc(pkt));
    }

    [Fact]
    public void ValidateCrc_TooShort_ReturnsFalse()
    {
        Assert.False(Protocol.ValidateCrc([0x55, 0x60, 0x01]));
    }

    // -----------------------------------------------------------------------
    // ResponseCmd
    // -----------------------------------------------------------------------

    [Fact]
    public void ResponseCmd_ClearsBit15()
    {
        ushort req = 0x8123;
        Assert.Equal((ushort)0x0123, Protocol.ResponseCmd(req));
    }

    [Fact]
    public void ResponseCmd_AlreadyClear_Unchanged()
    {
        ushort req = 0x0456;
        Assert.Equal(req, Protocol.ResponseCmd(req));
    }

    // -----------------------------------------------------------------------
    // ParseBattery
    // -----------------------------------------------------------------------

    [Fact]
    public void ParseBattery_ValidPacket_ReturnsCorrectLevels()
    {
        // 8-byte header + count(1) + 3 devices * 2 bytes + 2-byte CRC = 17
        byte[] pkt = new byte[17];
        pkt[0] = 0x55; pkt[1] = 0x60; pkt[2] = 0x01;
        pkt[5] = 7;    // payload len
        pkt[8] = 3;    // device count
        pkt[9]  = Protocol.DeviceLeft;  pkt[10] = 70;
        pkt[11] = Protocol.DeviceRight; pkt[12] = 65;
        pkt[13] = Protocol.DeviceCase;  pkt[14] = 50;
        ushort crc = Protocol.CalcCrc16(pkt.AsSpan(0, 15));
        pkt[15] = (byte)(crc & 0xFF); pkt[16] = (byte)(crc >> 8);

        var s = Protocol.ParseBattery(pkt);

        Assert.Equal(70, s.Left);
        Assert.Equal(65, s.Right);
        Assert.Equal(50, s.Case);
        Assert.False(s.LeftCharging);
        Assert.False(s.RightCharging);
        Assert.False(s.CaseCharging);
    }

    [Fact]
    public void ParseBattery_ChargingBitSet_ReturnsChargingTrue()
    {
        byte[] pkt = new byte[13];
        pkt[0] = 0x55; pkt[1] = 0x60; pkt[2] = 0x01;
        pkt[5] = 3;
        pkt[8] = 1;  // 1 device
        pkt[9]  = Protocol.DeviceLeft;
        pkt[10] = (byte)(72 | 0x80); // charging flag
        ushort crc = Protocol.CalcCrc16(pkt.AsSpan(0, 11));
        pkt[11] = (byte)(crc & 0xFF); pkt[12] = (byte)(crc >> 8);

        var s = Protocol.ParseBattery(pkt);

        Assert.Equal(72, s.Left);
        Assert.True(s.LeftCharging);
    }

    [Fact]
    public void ParseBattery_TooShort_ReturnsAllMinus1()
    {
        var s = Protocol.ParseBattery([0x55, 0x60]);
        Assert.Equal(-1, s.Left);
        Assert.Equal(-1, s.Right);
        Assert.Equal(-1, s.Case);
    }

    // -----------------------------------------------------------------------
    // ParseANC
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(AncMode.Off)]
    [InlineData(AncMode.High)]
    [InlineData(AncMode.Mid)]
    [InlineData(AncMode.Low)]
    [InlineData(AncMode.Adaptive)]
    [InlineData(AncMode.Transparency)]
    public void ParseANC_ReturnsModeAtByte9(AncMode mode)
    {
        byte[] pkt = new byte[10];
        pkt[9] = (byte)mode;
        Assert.Equal(mode, Protocol.ParseANC(pkt));
    }

    [Fact]
    public void ParseANC_TooShort_ReturnsOff()
    {
        Assert.Equal(AncMode.Off, Protocol.ParseANC([0x55, 0x60]));
    }

    // -----------------------------------------------------------------------
    // ParseListeningMode
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(6)]
    public void ParseListeningMode_ReturnsByteAt8(byte level)
    {
        byte[] pkt = new byte[10];
        pkt[8] = level;
        Assert.Equal(level, Protocol.ParseListeningMode(pkt));
    }

    // -----------------------------------------------------------------------
    // ParseUltraBass
    // -----------------------------------------------------------------------

    [Fact]
    public void ParseUltraBass_Enabled_Level3()
    {
        byte[] pkt = new byte[10];
        pkt[8] = 1;    // enabled
        pkt[9] = 6;    // rawLevel = 6 -> displayLevel = 3
        var (enabled, level) = Protocol.ParseUltraBass(pkt);
        Assert.True(enabled);
        Assert.Equal(3, level);
    }

    [Fact]
    public void ParseUltraBass_Disabled()
    {
        byte[] pkt = new byte[10];
        pkt[8] = 0;
        var (enabled, _) = Protocol.ParseUltraBass(pkt);
        Assert.False(enabled);
    }

    // -----------------------------------------------------------------------
    // ParseFirmware
    // -----------------------------------------------------------------------

    [Fact]
    public void ParseFirmware_NullTerminated_ReturnsCorrectString()
    {
        byte[] pkt = new byte[20];
        string ver = "1.2.3.4";
        System.Text.Encoding.ASCII.GetBytes(ver).CopyTo(pkt, 8);
        pkt[8 + ver.Length] = 0x00;
        Assert.Equal(ver, Protocol.ParseFirmware(pkt));
    }

    // -----------------------------------------------------------------------
    // CalcCrc16 (MODBUS)
    // -----------------------------------------------------------------------

    [Fact]
    public void CalcCrc16_EmptySpan_Returns0xFFFF()
    {
        // CRC16-MODBUS of empty input is the initial value 0xFFFF
        Assert.Equal(0xFFFF, Protocol.CalcCrc16([]));
    }

    [Fact]
    public void CalcCrc16_KnownVector()
    {
        // CRC16-MODBUS("123456789") = 0x4B37 -- standard test vector
        byte[] data = System.Text.Encoding.ASCII.GetBytes("123456789");
        Assert.Equal(0x4B37, Protocol.CalcCrc16(data));
    }
}
