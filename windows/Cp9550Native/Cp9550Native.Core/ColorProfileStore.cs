using System.Text;
using System.Text.Json;

namespace Cp9550Native.Core;

/// <summary>
/// ProgramData layout for the base ICC + user calibration publish path.
/// Effective profile for soft-proof/print is always <c>user/active.icc</c>.
/// </summary>
public static class ColorProfileStore
{
    public const string BaseFileName = "CP955_01.ICC";
    public const string ActiveFileName = "active.icc";

    private static readonly AsyncLocal<string?> RootOverride = new();

    /// <summary>
    /// Default: <c>%ProgramData%\CP9550\color</c>. Tests redirect via <see cref="UseRoot"/>.
    /// </summary>
    public static string RootDir =>
        RootOverride.Value
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "CP9550",
            "color");

    public static string BasePath => Path.Combine(RootDir, BaseFileName);
    public static string UserDir => Path.Combine(RootDir, "user");
    public static string ActivePath => Path.Combine(UserDir, ActiveFileName);
    public static string CalibrationMetaPath => Path.Combine(UserDir, "calibration.json");
    public static string UploadsDir => Path.Combine(UserDir, "uploads");

    /// <summary>
    /// Redirect the store root for the current async flow (unit tests).
    /// Does not touch live ProgramData when pointed at a temp directory.
    /// </summary>
    public static IDisposable UseRoot(string rootDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDir);
        Directory.CreateDirectory(rootDir);
        var previous = RootOverride.Value;
        RootOverride.Value = Path.GetFullPath(rootDir);
        return new RootScope(previous);
    }

    private sealed class RootScope(string? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            RootOverride.Value = previous;
        }
    }

    public static void EnsureLayout(string? packageBaseIccPath = null)
    {
        Directory.CreateDirectory(RootDir);
        Directory.CreateDirectory(UserDir);
        Directory.CreateDirectory(UploadsDir);

        if (!File.Exists(BasePath))
        {
            if (string.IsNullOrWhiteSpace(packageBaseIccPath) || !File.Exists(packageBaseIccPath))
                throw new FileNotFoundException(
                    $"Base ICC missing and no package source provided. Expected {BasePath}");
            File.Copy(packageBaseIccPath, BasePath, overwrite: false);
            try { File.SetAttributes(BasePath, FileAttributes.ReadOnly); } catch { /* best effort */ }
        }

        if (!File.Exists(ActivePath))
            ResetToBase();
    }

    /// <summary>Replace effective profile with a full printer ICC.</summary>
    public static string Replace(string sourceIccPath, string? method = "replace")
    {
        ValidateIcc(sourceIccPath);
        EnsureLayout();
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var archived = Path.Combine(UploadsDir, $"{stamp}-{Path.GetFileName(sourceIccPath)}");
        File.Copy(sourceIccPath, archived, overwrite: true);
        PublishActive(sourceIccPath);
        WriteMeta(new CalibrationMeta(
            Mode: "replace",
            Source: archived,
            AppliedUtc: DateTime.UtcNow,
            Method: method ?? "replace",
            Notes: "User ICC published as active.icc"));
        return ActivePath;
    }

    /// <summary>Restore base CP955_01 as the effective profile.</summary>
    public static string ResetToBase()
    {
        if (!File.Exists(BasePath))
            throw new FileNotFoundException($"Base ICC not found: {BasePath}");
        PublishActive(BasePath);
        WriteMeta(new CalibrationMeta(
            Mode: "reset",
            Source: BasePath,
            AppliedUtc: DateTime.UtcNow,
            Method: "reset",
            Notes: "Restored immutable base profile"));
        return ActivePath;
    }

    /// <summary>
    /// Layer hook (v1.1): for now archives the abstract/device-link and notes that
    /// composition must write a new effective ICC. Does not leave soft-proof on base
    /// while printing through a hidden LUT.
    /// </summary>
    public static string Layer(string overlayIccPath, string? notes = null)
    {
        ValidateIcc(overlayIccPath, allowLink: true);
        EnsureLayout();
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var archived = Path.Combine(UploadsDir, $"{stamp}-layer-{Path.GetFileName(overlayIccPath)}");
        File.Copy(overlayIccPath, archived, overwrite: true);

        // Spike: until LCMS2/WCS compose lands, publish overlay only if it is a full prtr
        // profile; otherwise keep active unchanged and record the pending layer.
        var deviceClass = ReadDeviceClass(overlayIccPath);
        if (deviceClass is "prtr" or "scnr" or "mntr" or "spac")
        {
            PublishActive(overlayIccPath);
            WriteMeta(new CalibrationMeta(
                Mode: "layer",
                Source: archived,
                AppliedUtc: DateTime.UtcNow,
                Method: "layer-passthrough",
                Notes: notes ?? "Overlay treated as effective ICC (compose not yet wired)"));
        }
        else
        {
            WriteMeta(new CalibrationMeta(
                Mode: "layer-pending",
                Source: archived,
                AppliedUtc: DateTime.UtcNow,
                Method: "layer-hook",
                Notes: notes ??
                    $"Device class '{deviceClass}' needs LCMS2/WCS compose → active.icc (Phase 3)"));
        }

        return ActivePath;
    }

    public static string ResolveEffectivePath()
    {
        EnsureLayout();
        return File.Exists(ActivePath) ? ActivePath : BasePath;
    }

    public static void ValidateIcc(string path, bool allowLink = false)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("ICC not found", path);

        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 128)
            throw new InvalidDataException("ICC too small to be valid.");
        if (Encoding.ASCII.GetString(bytes, 36, 4) != "acsp")
            throw new InvalidDataException("ICC magic 'acsp' missing.");

        var deviceClass = Encoding.ASCII.GetString(bytes, 12, 4);
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "prtr", "scnr", "mntr", "spac",
        };
        if (allowLink)
            allowed.Add("link");

        if (!allowed.Contains(deviceClass))
            throw new InvalidDataException(
                $"Unsupported ICC device class '{deviceClass}'. Prefer 'prtr'.");
    }

    public static string ReadDeviceClass(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return bytes.Length >= 16
            ? Encoding.ASCII.GetString(bytes, 12, 4)
            : "";
    }

    private static void PublishActive(string sourcePath)
    {
        Directory.CreateDirectory(UserDir);
        var tmp = ActivePath + ".tmp";
        File.Copy(sourcePath, tmp, overwrite: true);
        File.Copy(tmp, ActivePath, overwrite: true);
        File.Delete(tmp);
    }

    private static void WriteMeta(CalibrationMeta meta)
    {
        Directory.CreateDirectory(UserDir);
        var json = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(CalibrationMetaPath, json);
    }
}

public sealed record CalibrationMeta(
    string Mode,
    string Source,
    DateTime AppliedUtc,
    string Method,
    string? Notes);
