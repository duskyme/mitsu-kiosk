using System.Text;
using Xunit;

namespace Cp9550Native.Core.Tests;

public class ColorProfileStoreTests
{
    [Fact]
    public void ValidateIcc_rejects_missing_magic()
    {
        var dir = NewTemp();
        var bad = Path.Combine(dir, "bad.icc");
        var bytes = new byte[128];
        Encoding.ASCII.GetBytes("XXXX").CopyTo(bytes, 36);
        File.WriteAllBytes(bad, bytes);
        Assert.Throws<InvalidDataException>(() => ColorProfileStore.ValidateIcc(bad));
    }

    [Fact]
    public void Replace_and_Reset_publish_active_icc()
    {
        var root = NewTemp();
        using var _ = ColorProfileStore.UseRoot(root);

        var baseIcc = Path.Combine(root, "seed-base.icc");
        WriteMinimalPrtr(baseIcc);
        File.Copy(baseIcc, ColorProfileStore.BasePath);
        var userIcc = Path.Combine(root, "user-cal.icc");
        WriteMinimalPrtr(userIcc, mark: 0x42);

        ColorProfileStore.ValidateIcc(baseIcc);
        Assert.Equal("prtr", ColorProfileStore.ReadDeviceClass(baseIcc));

        var active = ColorProfileStore.Replace(userIcc);
        Assert.StartsWith(root, active, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(active));
        Assert.Equal(0x42, File.ReadAllBytes(active)[64]);

        ColorProfileStore.ResetToBase();
        Assert.Equal(0x00, File.ReadAllBytes(ColorProfileStore.ActivePath)[64]);
        Assert.True(File.Exists(ColorProfileStore.CalibrationMetaPath));
        Assert.DoesNotContain(
            Path.Combine("ProgramData", "CP9550"),
            ColorProfileStore.RootDir,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Layer_records_pending_for_link_class()
    {
        var root = NewTemp();
        using var _ = ColorProfileStore.UseRoot(root);

        WriteMinimalPrtr(ColorProfileStore.BasePath);
        ColorProfileStore.EnsureLayout();
        var before = File.ReadAllBytes(ColorProfileStore.ActivePath);

        var link = Path.Combine(root, "abs.link.icc");
        WriteMinimalIcc(link, deviceClass: "link");

        ColorProfileStore.Layer(link, notes: "unit-test pending compose");
        var meta = File.ReadAllText(ColorProfileStore.CalibrationMetaPath);
        Assert.Contains("layer-pending", meta);
        Assert.Equal(before, File.ReadAllBytes(ColorProfileStore.ActivePath));
        Assert.StartsWith(root, ColorProfileStore.RootDir, StringComparison.OrdinalIgnoreCase);
    }

    private static string NewTemp()
    {
        var d = Path.Combine(Path.GetTempPath(), "cp9550-icc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    private static void WriteMinimalPrtr(string path, byte mark = 0x00)
    {
        WriteMinimalIcc(path, "prtr", mark);
    }

    private static void WriteMinimalIcc(string path, string deviceClass, byte mark = 0x00)
    {
        var bytes = new byte[128];
        Encoding.ASCII.GetBytes(deviceClass).CopyTo(bytes, 12);
        Encoding.ASCII.GetBytes("acsp").CopyTo(bytes, 36);
        bytes[64] = mark;
        File.WriteAllBytes(path, bytes);
    }
}
