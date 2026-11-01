using System.Buffers.Binary;

namespace Cp9550Native.Core;

public enum QualityMode
{
    Fine,
    SuperFine,
    FineDeep,
}

public enum FitMode
{
    Cover,
    Contain,
}

public sealed record MediaPreset(
    string Key,
    string Label,
    string Ribbon,
    int MediaCode,
    int Cols,
    int Rows);

public static class MediaCatalog
{
    public static readonly MediaPreset FourBySix = new("4x6", "4×6\"", "CK9015", 0x02, 2152, 1416);
    public static readonly MediaPreset FiveBySeven = new("5x7", "5×7\"", "CK9318", 0x04, 1812, 2452);
    public static readonly MediaPreset SixByNine = new("6x9", "6×9\"", "CK9523", 0x05, 2152, 2792);

    public static IReadOnlyList<MediaPreset> All { get; } =
        new[] { FourBySix, FiveBySeven, SixByNine };

    public static MediaPreset ByKey(string key) =>
        All.FirstOrDefault(m => m.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"Unknown media '{key}'", nameof(key));
}

public static class UsbIds
{
    public const int VendorId = 0x06D3;
    public const int ProductIdCp9550DwS = 0x03A5;
}

public static class Protocol
{
    public static readonly byte[] Footer9550S = { 0x1B, 0x50, 0x47, 0x00 };
    public static readonly byte[] ClearMemoryS = { 0x1B, 0x5A, 0x43, 0x00 };
    /// <summary>CP-9550DW-S only: must precede job headers (selphy_print).</summary>
    public static readonly byte[] StartJobS = { 0x1B, 0x44, 0x00, 0x00 };
    public static readonly byte[] InitCmd1 = { 0x1B, 0x53, 0xC5, 0x9D };
    public static readonly byte[] InitCmd2 = { 0x1B, 0x4B, 0x7F, 0x00 };
    public static readonly byte[] CmdMedia = { 0x1B, 0x56, 0x24, 0x00 };
    public static readonly byte[] CmdStatus = { 0x1B, 0x56, 0x30, 0x00 };
    public static readonly byte[] CmdStatusB = { 0x1B, 0x56, 0x21, 0x00 };
    public static readonly byte[] CmdSerial = { 0x1B, 0x72, 0x6E, 0x00 };

    public static byte[] BuildHeaders(MediaPreset media, QualityMode quality, int copies, bool cut2x6 = false)
    {
        copies = Math.Clamp(copies, 1, 680);
        var mode = quality == QualityMode.SuperFine ? (byte)0x80 : (byte)0x00;
        var fineDeep = quality == QualityMode.FineDeep ? (byte)0x01 : (byte)0x00;
        var cut = cut2x6 ? (byte)0x83 : (byte)0x00;

        var hdr1 = new byte[50];
        hdr1[0] = 0x1B; hdr1[1] = 0x57; hdr1[2] = 0x20; hdr1[3] = 0x2E;
        hdr1[4] = 0x00; hdr1[5] = 0x0A; hdr1[6] = 0x10;
        BinaryPrimitives.WriteUInt16BigEndian(hdr1.AsSpan(14), (ushort)media.Cols);
        BinaryPrimitives.WriteUInt16BigEndian(hdr1.AsSpan(16), (ushort)media.Rows);

        var hdr2 = new byte[50];
        ReadOnlySpan<byte> hdr2Prefix =
        [
            0x1B, 0x57, 0x21, 0x2E, 0x00, 0x80, 0x00, 0x22, 0x08, 0x03,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
        ];
        hdr2Prefix.CopyTo(hdr2);
        BinaryPrimitives.WriteUInt16BigEndian(hdr2.AsSpan(28), (ushort)copies);
        hdr2[32] = cut;
        hdr2[38] = mode;
        hdr2[49] = 0x01;

        var hdr3 = new byte[50];
        hdr3[0] = 0x1B; hdr3[1] = 0x57; hdr3[2] = 0x22; hdr3[3] = 0x2E;
        hdr3[5] = 0x40;
        hdr3[11] = fineDeep;

        var all = new byte[150];
        Buffer.BlockCopy(hdr1, 0, all, 0, 50);
        Buffer.BlockCopy(hdr2, 0, all, 50, 50);
        Buffer.BlockCopy(hdr3, 0, all, 100, 50);
        return all;
    }

    public static (byte[] B, byte[] G, byte[] R) SplitPlanarBgr(ReadOnlySpan<byte> rgb, int cols, int rows)
    {
        var plane = cols * rows;
        if (rgb.Length < plane * 3)
            throw new ArgumentException($"Need {plane * 3} RGB bytes, got {rgb.Length}");

        var b = new byte[plane];
        var g = new byte[plane];
        var r = new byte[plane];
        for (var i = 0; i < plane; i++)
        {
            var o = i * 3;
            r[i] = rgb[o];
            g[i] = rgb[o + 1];
            b[i] = rgb[o + 2];
        }
        return (b, g, r);
    }

    private static byte[] PlaneBlock(byte[] plane, int cols, int rows)
    {
        var block = new byte[12 + plane.Length];
        block[0] = 0x1B; block[1] = 0x5A; block[2] = 0x54; block[3] = 0x00;
        BinaryPrimitives.WriteUInt16BigEndian(block.AsSpan(8), (ushort)cols);
        BinaryPrimitives.WriteUInt16BigEndian(block.AsSpan(10), (ushort)rows);
        Buffer.BlockCopy(plane, 0, block, 12, plane.Length);
        return block;
    }

    public static byte[] BuildSpool(ReadOnlySpan<byte> rgb, MediaPreset media, QualityMode quality, int copies = 1)
    {
        var (b, g, r) = SplitPlanarBgr(rgb, media.Cols, media.Rows);
        var headers = BuildHeaders(media, quality, copies);
        var planeB = PlaneBlock(b, media.Cols, media.Rows);
        var planeG = PlaneBlock(g, media.Cols, media.Rows);
        var planeR = PlaneBlock(r, media.Cols, media.Rows);

        var total = headers.Length + planeB.Length + planeG.Length + planeR.Length + Footer9550S.Length;
        var spool = new byte[total];
        var o = 0;
        Buffer.BlockCopy(headers, 0, spool, o, headers.Length); o += headers.Length;
        Buffer.BlockCopy(planeB, 0, spool, o, planeB.Length); o += planeB.Length;
        Buffer.BlockCopy(planeG, 0, spool, o, planeG.Length); o += planeG.Length;
        Buffer.BlockCopy(planeR, 0, spool, o, planeR.Length); o += planeR.Length;
        Buffer.BlockCopy(Footer9550S, 0, spool, o, Footer9550S.Length);
        return spool;
    }

    public static MediaStatus? ParseMediaStatus(ReadOnlySpan<byte> buf)
    {
        if (buf.Length < 48 || buf[0] != 0x24 || buf[1] != 0x2E)
            return null;

        var type = buf[14];
        var max = BinaryPrimitives.ReadUInt16BigEndian(buf.Slice(28, 2));
        var remain = BinaryPrimitives.ReadUInt16BigEndian(buf.Slice(32, 2));
        var code = type & 0x0F;
        var name = code switch
        {
            0x02 => "CK9015 (4×6)",
            0x04 => "CK9318 (5×7)",
            0x05 => "CK9523 (6×9)",
            _ => $"Unknown (0x{type:X2})",
        };
        return new MediaStatus(type, max, remain, name);
    }

    /// <summary>
    /// Status (0x56 0x30) layout from selphy_print <c>mitsu9550_status</c>:
    /// sts1@6 (0=idle), sts3@16, sts4@17, sts5@18 (0=ready for next job).
    /// </summary>
    public static PrinterStatus? ParsePrinterStatus(ReadOnlySpan<byte> buf)
    {
        if (buf.Length < 19 || buf[0] != 0x30 || buf[1] != 0x2E)
            return null;
        var copies = BinaryPrimitives.ReadUInt16BigEndian(buf.Slice(8, 2));
        return new PrinterStatus(buf[6], buf[16], buf[17], buf[18], copies);
    }

    /// <summary>
    /// Serial reply layout observed on CP-9550DW-S:
    ///   E4 72 6E 00 | len | UTF-16LE payload
    /// (0x72 0x6E echoes the "rn" serial command).
    /// </summary>
    public static bool LooksLikeSerial(ReadOnlySpan<byte> buf) =>
        buf.Length >= 5 && buf[1] == 0x72 && buf[2] == 0x6E && buf[3] == 0x00;

    public static string? ParseSerial(ReadOnlySpan<byte> buf)
    {
        if (!LooksLikeSerial(buf))
            return null;
        var length = buf[4];
        if (5 + length > buf.Length)
            return null;
        try
        {
            return System.Text.Encoding.Unicode.GetString(buf.Slice(5, length)).Trim();
        }
        catch
        {
            return null;
        }
    }

    public static string DescribeUnexpectedReply(ReadOnlySpan<byte> buf)
    {
        var hex = Convert.ToHexString(buf[..Math.Min(16, buf.Length)]);
        if (LooksLikeSerial(buf))
        {
            var serial = ParseSerial(buf);
            return serial is null
                ? $"sériová odpoveď namiesto media (hex {hex})"
                : $"sériová odpoveď namiesto media (\"{serial}\", hex {hex})";
        }

        if (buf.Length >= 2 && buf[0] == 0x24 && buf[1] == 0x2E)
            return $"neúplná media odpoveď ({buf.Length} B, hex {hex})";

        return $"neznáma odpoveď (hex {hex})";
    }
}

public sealed record MediaStatus(int Type, int Max, int Remain, string Name);

/// <param name="Sts1">MM — 0 = idle / print finished.</param>
/// <param name="Sts3">QQ — selphy expects bits 0xC0 set before starting print.</param>
/// <param name="Sts5">SS — 0 = ready to accept another job.</param>
public sealed record PrinterStatus(byte Sts1, byte Sts3, byte Sts4, byte Sts5, int Copies)
{
    public bool IsIdle => Sts1 == 0;
    public bool ReadyForJob => Sts5 == 0;
    public bool CanStartPrint => ReadyForJob && (Sts3 & 0xC0) != 0;

    public string Summary =>
        $"sts1=0x{Sts1:X2} sts3=0x{Sts3:X2} sts5=0x{Sts5:X2} copies={Copies}";
}
