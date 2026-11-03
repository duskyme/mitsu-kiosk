namespace Cp9550BridgeTray;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using var mutex = new Mutex(true, @"Local\Cp9550BridgeTray", out var created);
        if (!created)
        {
            MessageBox.Show(
                "CP-9550 Bridge Tray is already running (notification area).",
                "CP-9550 Bridge Tray",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var opts = TrayOptions.Parse(args);
        Application.Run(new TrayApplicationContext(opts));
    }
}

internal sealed class TrayOptions
{
    public int Port { get; init; } = 9100;
    public bool AutoStart { get; init; } = true;
    public bool DryRun { get; init; }
    public string? BridgeExe { get; init; }
    public string? SaveDir { get; init; }

    public static TrayOptions Parse(string[] args)
    {
        var port = 9100;
        var auto = true;
        var dry = false;
        string? exe = null;
        string? save = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--port":
                    port = int.Parse(args[++i]);
                    break;
                case "--no-auto":
                    auto = false;
                    break;
                case "--dry-run":
                    dry = true;
                    break;
                case "--bridge-exe":
                    exe = args[++i];
                    break;
                case "--save-dir":
                    save = args[++i];
                    break;
                case "--no-save-dir":
                    save = "";
                    break;
                case "-h" or "--help" or "/?":
                    MessageBox.Show(
                        "Cp9550BridgeTray — notification-area status for SpoolBridge\n\n" +
                        "  --port N          Bridge TCP port (default 9100)\n" +
                        "  --no-auto         Do not auto-start/stop on USB plug\n" +
                        "  --dry-run         Pass --dry-run to the bridge (lab only)\n" +
                        "  --bridge-exe PATH Explicit Cp9550SpoolBridge.exe\n" +
                        "  --save-dir DIR    Pass --save-dir to the bridge\n" +
                        "  --no-save-dir     Disable default last-spool capture dir\n",
                        "CP-9550 Bridge Tray");
                    Environment.Exit(0);
                    break;
            }
        }

        // Default: keep last few spools under ProgramData for reject diagnostics (no UAC).
        if (save is null)
        {
            save = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "CP9550",
                "spool-capture");
        }
        else if (save.Length == 0)
        {
            save = null;
        }

        return new TrayOptions
        {
            Port = port,
            AutoStart = auto,
            DryRun = dry,
            BridgeExe = exe,
            SaveDir = save,
        };
    }
}
