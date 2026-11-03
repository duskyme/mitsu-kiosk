using System.Text.Json;

namespace Cp9550BridgeTray;

internal sealed class BridgeStatusDto
{
    public string State { get; set; } = "stopped";
    public int Port { get; set; }
    public bool DryRun { get; set; }
    public bool UsbPresent { get; set; }
    public int? Pid { get; set; }
    public string? LastError { get; set; }
    public string? UpdatedUtc { get; set; }
}

internal static class BridgeStatus
{
    public static string DefaultPath { get; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "CP9550",
            "bridge-status.json");

    public static BridgeStatusDto? TryRead()
    {
        try
        {
            if (!File.Exists(DefaultPath))
                return null;
            var json = File.ReadAllText(DefaultPath);
            return JsonSerializer.Deserialize<BridgeStatusDto>(json);
        }
        catch
        {
            return null;
        }
    }
}
