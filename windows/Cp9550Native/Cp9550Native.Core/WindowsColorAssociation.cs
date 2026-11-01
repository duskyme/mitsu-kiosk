using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Cp9550Native.Core;

/// <summary>
/// Thin wrapper around MSCMS InstallColorProfile / AssociateColorProfileWithDevice
/// so soft-proof apps see the same effective ICC the print path uses.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsColorAssociation
{
    public static void InstallAndAssociate(string iccPath, string deviceName)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Color association requires Windows.");

        var full = Path.GetFullPath(iccPath);
        if (!File.Exists(full))
            throw new FileNotFoundException("ICC not found", full);
        if (string.IsNullOrWhiteSpace(deviceName))
            throw new ArgumentException("Printer device/queue name required.", nameof(deviceName));

        ColorProfileStore.ValidateIcc(full);

        if (!InstallColorProfile(null, full))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                $"InstallColorProfile failed for {full}");

        var profileName = Path.GetFileName(full);
        if (!AssociateColorProfileWithDevice(null, profileName, deviceName))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                $"AssociateColorProfileWithDevice failed for '{deviceName}' ← {profileName}");
    }

    public static void Disassociate(string profileFileName, string deviceName)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Color association requires Windows.");

        if (!DisassociateColorProfileFromDevice(null, profileFileName, deviceName))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                $"DisassociateColorProfileFromDevice failed for '{deviceName}'");
    }

    /// <summary>
    /// Publish <paramref name="effectiveIccPath"/> into ProgramData active.icc, install it,
    /// and associate with the printer queue name used in Color Management.
    /// </summary>
    public static string PublishAssociate(
        string effectiveIccPath,
        string deviceName,
        string mode = "replace")
    {
        ColorProfileStore.EnsureLayout();
        var active = mode.Equals("reset", StringComparison.OrdinalIgnoreCase)
            ? ColorProfileStore.ResetToBase()
            : mode.Equals("layer", StringComparison.OrdinalIgnoreCase)
                ? ColorProfileStore.Layer(effectiveIccPath)
                : ColorProfileStore.Replace(effectiveIccPath);

        // Copy active into the system color directory name Windows enumerates easily.
        var colorDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "spool", "drivers", "color");
        Directory.CreateDirectory(colorDir);
        var installedName = "CP9550_active.icc";
        var installedPath = Path.Combine(colorDir, installedName);
        File.Copy(active, installedPath, overwrite: true);

        InstallAndAssociate(installedPath, deviceName);
        return installedPath;
    }

    [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool InstallColorProfile(string? machineName, string profileName);

    [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool AssociateColorProfileWithDevice(
        string? machineName,
        string profileName,
        string deviceName);

    [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool DisassociateColorProfileFromDevice(
        string? machineName,
        string profileName,
        string deviceName);
}
