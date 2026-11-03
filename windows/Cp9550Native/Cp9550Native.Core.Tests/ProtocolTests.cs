using Cp9550Native.Core;
using Xunit;

namespace Cp9550Native.Core.Tests;

public class ProtocolTests
{
    [Fact]
    public void Headers_HaveExpectedLayout()
    {
        var media = MediaCatalog.FourBySix;
        var hdr = Protocol.BuildHeaders(media, QualityMode.SuperFine, copies: 2);
        Assert.Equal(150, hdr.Length);
        Assert.Equal(new byte[] { 0x1B, 0x57, 0x20, 0x2E }, hdr[..4]);
        Assert.Equal(0x08, hdr[14]);
        Assert.Equal(0x68, hdr[15]); // 2152
        Assert.Equal(0x05, hdr[16]);
        Assert.Equal(0x88, hdr[17]); // 1416
        Assert.Equal(0x80, hdr[50 + 38]); // superfine
        Assert.Equal(0x00, hdr[50 + 28]);
        Assert.Equal(0x02, hdr[50 + 29]); // copies
    }

    [Fact]
    public void Spool_SizeMatchesPlanarLayout()
    {
        var media = MediaCatalog.FourBySix;
        var plane = media.Cols * media.Rows;
        var rgb = new byte[plane * 3];
        for (var i = 0; i < plane; i++)
        {
            rgb[i * 3] = 10;
            rgb[i * 3 + 1] = 20;
            rgb[i * 3 + 2] = 30;
        }

        var spool = Protocol.BuildSpool(rgb, media, QualityMode.Fine, 1);
        var expected = 150 + 3 * (12 + plane) + 4;
        Assert.Equal(expected, spool.Length);
        Assert.Equal(Protocol.Footer9550S, spool[^4..]);
        Assert.Equal(new byte[] { 0x1B, 0x5A, 0x54, 0x00 }, spool.AsSpan(150, 4).ToArray());
        // Planar RGB: first plane payload is Red (10), not Blue (30).
        Assert.Equal(10, spool[150 + 12]);
        Assert.Equal(20, spool[150 + 12 + plane + 12]);
        Assert.Equal(30, spool[150 + 12 + 2 * (plane + 12)]);
    }

    [Fact]
    public void SplitPlanarRgb_KeepsChannelOrder()
    {
        var rgb = new byte[] { 10, 20, 30 };
        var (r, g, b) = Protocol.SplitPlanarRgb(rgb, 1, 1);
        Assert.Equal(new byte[] { 10 }, r);
        Assert.Equal(new byte[] { 20 }, g);
        Assert.Equal(new byte[] { 30 }, b);
    }

    [Fact]
    public void ParseMediaStatus_ReadsRibbonCounters()
    {
        var buf = new byte[48];
        buf[0] = 0x24;
        buf[1] = 0x2E;
        buf[14] = 0x02;
        buf[28] = 0x01;
        buf[29] = 0x90; // 400
        buf[32] = 0x01;
        buf[33] = 0x2C; // 300
        var parsed = Protocol.ParseMediaStatus(buf);
        Assert.NotNull(parsed);
        Assert.Equal(400, parsed!.Max);
        Assert.Equal(300, parsed.Remain);
        Assert.Contains("4×6", parsed.Name);
    }

    [Fact]
    public void ParseSerial_ReadsUtf16Payload()
    {
        // Live CP-9550DW-S reply: E4 72 6E 00 | len | UTF-16LE "9550Z D33364"
        var payload = System.Text.Encoding.Unicode.GetBytes("9550Z D33364");
        var buf = new byte[5 + payload.Length];
        buf[0] = 0xE4;
        buf[1] = 0x72;
        buf[2] = 0x6E;
        buf[3] = 0x00;
        buf[4] = (byte)payload.Length;
        Buffer.BlockCopy(payload, 0, buf, 5, payload.Length);

        Assert.True(Protocol.LooksLikeSerial(buf));
        Assert.Equal("9550Z D33364", Protocol.ParseSerial(buf));
        Assert.Null(Protocol.ParseMediaStatus(buf));
        Assert.Contains("sériová odpoveď", Protocol.DescribeUnexpectedReply(buf));
    }

    [Fact]
    public void ParseMediaStatus_RejectsSerialPacket()
    {
        var junk = Convert.FromHexString("E4726E001839003500350030005A0020");
        Assert.Null(Protocol.ParseMediaStatus(junk));
        Assert.True(Protocol.LooksLikeSerial(junk));
    }

    [Fact]
    public void ParsePrinterStatus_UsesSelphyOffsets()
    {
        // Live idle status: sts1@6=00, sts3@16=3E, sts5@18=00
        var buf = Convert.FromHexString(
            "302E00000000000000000000000000003E0000000000000000000000000000000000000000000000FFE40B0079380000");
        var sts = Protocol.ParsePrinterStatus(buf);
        Assert.NotNull(sts);
        Assert.Equal(0x00, sts!.Sts1);
        Assert.Equal(0x3E, sts.Sts3);
        Assert.Equal(0x00, sts.Sts5);
        Assert.True(sts.IsIdle);
        Assert.True(sts.ReadyForJob);
        // Old bug: treating index 16 as sts5 would see 0x3E and never go idle/ready.
        Assert.NotEqual(0, buf[16]);
        // Idle has no 0x40/0x80 — those appear only after planes are accepted.
        Assert.False(sts.CanStartPrint);
    }

    [Theory]
    [InlineData(0x02, 2152, 1416, true)]
    [InlineData(0x02, 2152, 1184, true)]
    [InlineData(0x04, 1812, 2452, true)]
    [InlineData(0x05, 2152, 2792, true)]
    [InlineData(0x02, 1812, 2452, false)]
    [InlineData(0x04, 2152, 1416, false)]
    [InlineData(0x05, 2152, 1000, false)]
    public void MediaAcceptsFrame_MatchesSelphy9550S(int type, int cols, int rows, bool ok)
    {
        Assert.Equal(ok, Protocol.MediaAcceptsFrame(type, cols, rows));
    }

    [Fact]
    public void ReadHeaderFrame_ReadsColsRows()
    {
        var hdr = Protocol.BuildHeaders(MediaCatalog.FiveBySeven, QualityMode.Fine, 1);
        var (cols, rows) = Protocol.ReadHeaderFrame(hdr);
        Assert.Equal(1812, cols);
        Assert.Equal(2452, rows);
    }
}
