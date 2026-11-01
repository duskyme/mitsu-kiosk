using System.Diagnostics;
using Cp9550Native.Core;
using Xunit;

namespace Cp9550Native.Core.Tests;

/// <summary>
/// Cross-check C# <see cref="Protocol.BuildSpool"/> against the Phase 1 native
/// <c>cp9550-spooltool</c> (same planar RGB layout as the XPS filter).
/// </summary>
public class NativeSpoolParityTests
{
    private static string? FindSpoolTool()
    {
        var root = FindRepoRoot();
        if (root is null)
            return null;
        var candidates = new[]
        {
            Path.Combine(root, "windows", "v4-driver", "filter", "_out", "Release", "cp9550-spooltool.exe"),
            Path.Combine(root, "windows", "v4-driver", "filter", "_out", "Debug", "cp9550-spooltool.exe"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, ".gitignore")) &&
                Directory.Exists(Path.Combine(dir.FullName, "windows", "v4-driver")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    [Fact]
    public void Spooltool_Matches_Protocol_BuildSpool_SolidRgb()
    {
        var tool = FindSpoolTool();
        if (tool is null)
        {
            // Filter native artifacts are built via windows/v4-driver/filter/build.ps1.
            // Skip rather than fail pure-dotnet CI without MSVC.
            return;
        }

        var media = MediaCatalog.FourBySix;
        var plane = media.Cols * media.Rows;
        var rgb = new byte[plane * 3];
        for (var i = 0; i < plane; i++)
        {
            rgb[i * 3] = 10;
            rgb[i * 3 + 1] = 20;
            rgb[i * 3 + 2] = 30;
        }

        var csharp = Protocol.BuildSpool(rgb, media, QualityMode.Fine, 1);

        var tmp = Path.Combine(Path.GetTempPath(), "cp9550-parity-" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = tool,
                ArgumentList = { "--solid", "10", "20", "30", "--quality", "fine", "--out", tmp },
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("failed to start spooltool");
            var stderr = p.StandardError.ReadToEnd();
            Assert.True(p.WaitForExit(120_000), "spooltool timed out");
            Assert.True(p.ExitCode == 0, "spooltool failed: " + stderr);

            var native = File.ReadAllBytes(tmp);
            Assert.Equal(csharp.Length, native.Length);
            Assert.True(csharp.AsSpan().SequenceEqual(native), "native spool bytes diverge from Protocol.BuildSpool");
        }
        finally
        {
            if (File.Exists(tmp))
                File.Delete(tmp);
        }
    }
}
