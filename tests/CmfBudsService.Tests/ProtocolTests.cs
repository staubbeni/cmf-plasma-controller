using CmfBudsService;
using Xunit;

namespace CmfBudsService.Tests;

/// <summary>
/// Unit tests for <see cref="Protocol"/> — verifies that every packet the
/// daemon sends to the hardware has the correct wire format and checksum,
/// and that battery response parsing is accurate.
/// </summary>
public class ProtocolTests
{
    // -----------------------------------------------------------------------
    // ANC packet tests
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildAncPacket_Off_HasCorrectBytes()
    {
        byte[] packet = Protocol.BuildAncPacket(AncMode.Off);

        Assert.Equal(4, packet.Length);
        Assert.Equal(0x55, packet[0]); // preamble
        Assert.Equal(0x60, packet[1]); // ANC command
        Assert.Equal(0x00, packet[2]); // Off mode
    }

    [Fact]
    public void BuildAncPacket_ANC_HasCorrectBytes()
    {
        byte[] packet = Protocol.BuildAncPacket(AncMode.ANC);

        Assert.Equal(4, packet.Length);
        Assert.Equal(0x55, packet[0]);
        Assert.Equal(0x60, packet[1]);
        Assert.Equal(0x01, packet[2]);
    }

    [Fact]
    public void BuildAncPacket_Transparency_HasCorrectBytes()
    {
        byte[] packet = Protocol.BuildAncPacket(AncMode.Transparency);

        Assert.Equal(4, packet.Length);
        Assert.Equal(0x55, packet[0]);
        Assert.Equal(0x60, packet[1]);
        Assert.Equal(0x02, packet[2]);
    }

    [Theory]
    [InlineData(AncMode.Off)]
    [InlineData(AncMode.ANC)]
    [InlineData(AncMode.Transparency)]
    public void BuildAncPacket_ChecksumIsValid(AncMode mode)
    {
        byte[] packet = Protocol.BuildAncPacket(mode);
        Assert.True(Protocol.VerifyChecksum(packet), $"Checksum invalid for mode {mode}");
    }

    [Theory]
    [InlineData(AncMode.Off,          0x55 ^ 0x60 ^ 0x00)]
    [InlineData(AncMode.ANC,          0x55 ^ 0x60 ^ 0x01)]
    [InlineData(AncMode.Transparency, 0x55 ^ 0x60 ^ 0x02)]
    public void BuildAncPacket_ChecksumValueMatchesXorOfPrecedingBytes(AncMode mode, byte expected)
    {
        byte[] packet = Protocol.BuildAncPacket(mode);
        Assert.Equal(expected, packet[3]);
    }

    // -----------------------------------------------------------------------
    // Battery request packet tests
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildBatteryRequestPacket_HasCorrectBytes()
    {
        byte[] packet = Protocol.BuildBatteryRequestPacket();

        Assert.Equal(3, packet.Length);
        Assert.Equal(0x55, packet[0]);
        Assert.Equal(0x61, packet[1]);
    }

    [Fact]
    public void BuildBatteryRequestPacket_ChecksumIsValid()
    {
        byte[] packet = Protocol.BuildBatteryRequestPacket();
        Assert.True(Protocol.VerifyChecksum(packet));
    }

    // -----------------------------------------------------------------------
    // Battery response parsing tests
    // -----------------------------------------------------------------------

    [Fact]
    public void ParseBatteryResponse_ValidPacket_ReturnsCorrectLevels()
    {
        // Construct a valid response: [0x55, 0x61, left, right, case, checksum]
        byte left  = 72;
        byte right = 68;
        byte cas   = 55;
        byte[] response = [0x55, 0x61, left, right, cas, 0x00];
        response[^1] = XorChecksum(response[..^1]);

        var (l, r, c) = Protocol.ParseBatteryResponse(response);

        Assert.Equal(left,  l);
        Assert.Equal(right, r);
        Assert.Equal(cas,   c);
    }

    [Fact]
    public void ParseBatteryResponse_TooShort_ReturnsMinus1()
    {
        byte[] tooShort = [0x55, 0x61, 50];
        var (l, r, c) = Protocol.ParseBatteryResponse(tooShort);

        Assert.Equal(-1, l);
        Assert.Equal(-1, r);
        Assert.Equal(-1, c);
    }

    [Fact]
    public void ParseBatteryResponse_Null_ReturnsMinus1()
    {
        var (l, r, c) = Protocol.ParseBatteryResponse(null!);

        Assert.Equal(-1, l);
        Assert.Equal(-1, r);
        Assert.Equal(-1, c);
    }

    [Fact]
    public void ParseBatteryResponse_WrongPreamble_ReturnsMinus1()
    {
        byte[] bad = [0xAA, 0x61, 70, 70, 70, 0x00];
        bad[^1] = XorChecksum(bad[..^1]);

        var (l, r, c) = Protocol.ParseBatteryResponse(bad);

        Assert.Equal(-1, l);
        Assert.Equal(-1, r);
        Assert.Equal(-1, c);
    }

    [Fact]
    public void ParseBatteryResponse_WrongCommand_ReturnsMinus1()
    {
        byte[] bad = [0x55, 0x60, 70, 70, 70, 0x00];
        bad[^1] = XorChecksum(bad[..^1]);

        var (l, r, c) = Protocol.ParseBatteryResponse(bad);

        Assert.Equal(-1, l);
        Assert.Equal(-1, r);
        Assert.Equal(-1, c);
    }

    [Fact]
    public void ParseBatteryResponse_BadChecksum_ReturnsMinus1()
    {
        byte[] bad = [0x55, 0x61, 70, 70, 70, 0xFF]; // deliberately wrong checksum

        var (l, r, c) = Protocol.ParseBatteryResponse(bad);

        Assert.Equal(-1, l);
        Assert.Equal(-1, r);
        Assert.Equal(-1, c);
    }

    // -----------------------------------------------------------------------
    // VerifyChecksum tests
    // -----------------------------------------------------------------------

    [Fact]
    public void VerifyChecksum_ValidPacket_ReturnsTrue()
    {
        byte[] packet = Protocol.BuildAncPacket(AncMode.ANC);
        Assert.True(Protocol.VerifyChecksum(packet));
    }

    [Fact]
    public void VerifyChecksum_CorruptedByte_ReturnsFalse()
    {
        byte[] packet = Protocol.BuildAncPacket(AncMode.ANC);
        packet[2] ^= 0xFF; // corrupt mode byte
        Assert.False(Protocol.VerifyChecksum(packet));
    }

    [Fact]
    public void VerifyChecksum_NullPacket_ReturnsFalse()
    {
        Assert.False(Protocol.VerifyChecksum(null!));
    }

    [Fact]
    public void VerifyChecksum_SingleByte_ReturnsFalse()
    {
        Assert.False(Protocol.VerifyChecksum([0x55]));
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static byte XorChecksum(byte[] bytes)
    {
        byte r = 0;
        foreach (byte b in bytes) r ^= b;
        return r;
    }
}
