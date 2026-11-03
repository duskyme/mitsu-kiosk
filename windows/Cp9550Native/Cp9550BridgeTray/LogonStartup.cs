using Microsoft.Win32;

namespace Cp9550BridgeTray;

/// <summary>
/// Optional per-user logon registration via HKCU Run (no elevation / no UAC).
/// Distinct from USB "Auto-start on USB" which only controls SpoolBridge while the tray runs.
/// </summary>
internal static class LogonStartup
{
    public const string ValueName = "CP9550BridgeTray";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string s && !string.IsNullOrWhiteSpace(s);
    }

    public static void Register(TrayOptions opts)
    {
        var exe = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot resolve tray executable path.");
        var cmd = BuildCommandLine(exe, opts);
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath)
            ?? throw new InvalidOperationException($"Cannot open HKCU\\{RunKeyPath}");
        key.SetValue(ValueName, cmd, RegistryValueKind.String);
    }

    public static void Unregister()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    public static string BuildCommandLine(string trayExe, TrayOptions opts)
    {
        var parts = new List<string> { Quote(trayExe) };
        if (opts.Port != 9100)
            parts.Add($"--port {opts.Port}");
        if (!opts.AutoStart)
            parts.Add("--no-auto");
        if (opts.DryRun)
            parts.Add("--dry-run");
        if (!string.IsNullOrWhiteSpace(opts.BridgeExe))
            parts.Add($"--bridge-exe {Quote(opts.BridgeExe)}");
        if (opts.SaveDir is null)
            parts.Add("--no-save-dir");
        else if (!IsDefaultSaveDir(opts.SaveDir))
            parts.Add($"--save-dir {Quote(opts.SaveDir)}");
        return string.Join(' ', parts);
    }

    private static bool IsDefaultSaveDir(string saveDir)
    {
        var def = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "CP9550",
            "spool-capture");
        return string.Equals(
            Path.GetFullPath(saveDir).TrimEnd('\\'),
            Path.GetFullPath(def).TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string Quote(string path) =>
        path.Contains(' ', StringComparison.Ordinal) || path.Contains('"', StringComparison.Ordinal)
            ? "\"" + path.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
            : path;
}
