namespace Cp9550BridgeTray;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly TrayOptions _opts;
    private readonly NotifyIcon _icon;
    private readonly BridgeProcessManager _bridge;
    private readonly UsbPresenceMonitor _usb;
    private readonly System.Windows.Forms.Timer _uiTimer;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _autoItem;
    private readonly ToolStripMenuItem _logonItem;
    private string? _lastHealthNote;

    public TrayApplicationContext(TrayOptions opts)
    {
        _opts = opts;
        _bridge = new BridgeProcessManager(opts);
        _usb = new UsbPresenceMonitor();

        _statusItem = new ToolStripMenuItem("Status: starting…") { Enabled = false };
        _autoItem = new ToolStripMenuItem("Auto-start on USB")
        {
            Checked = opts.AutoStart,
            CheckOnClick = true,
        };
        _autoItem.CheckedChanged += (_, _) =>
        {
            if (_autoItem.Checked && _usb.IsPresent)
                TryStartBridge("USB present (auto on)");
            else if (!_autoItem.Checked)
            {
                /* leave running; user can Stop manually */
            }
        };

        _logonItem = new ToolStripMenuItem("Start with Windows (logon)")
        {
            Checked = LogonStartup.IsRegistered(),
            CheckOnClick = true,
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_autoItem);
        menu.Items.Add(_logonItem);
        menu.Items.Add("Start bridge", null, (_, _) => TryStartBridge("manual"));
        menu.Items.Add("Restart bridge", null, (_, _) =>
        {
            try
            {
                _bridge.Restart();
                RefreshUi("restarted");
            }
            catch (Exception ex)
            {
                ShowErrorBalloon(ex.Message);
                RefreshUi("error: " + ex.Message);
            }
        });
        menu.Items.Add("Clean zombies", null, (_, _) =>
        {
            var n = _bridge.CleanupZombies();
            var note = n > 0 ? $"cleaned {n} zombie(s)" : "no zombies";
            if (_autoItem.Checked && _usb.IsPresent)
                TryStartBridge(note);
            else
                RefreshUi(note);
        });
        menu.Items.Add("Stop bridge", null, (_, _) =>
        {
            _bridge.Stop();
            RefreshUi("stopped");
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "CP-9550 Spool Bridge",
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => TryStartBridge("double-click");

        _logonItem.CheckedChanged += (_, _) =>
        {
            try
            {
                if (_logonItem.Checked)
                    LogonStartup.Register(_opts);
                else
                    LogonStartup.Unregister();
            }
            catch (Exception ex)
            {
                _logonItem.Checked = LogonStartup.IsRegistered();
                ShowErrorBalloon("Logon startup: " + ex.Message);
            }
        };

        _usb.PresenceChanged += OnUsbChanged;
        _usb.Start();

        _uiTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _uiTimer.Tick += (_, _) =>
        {
            var want = _autoItem.Checked && _usb.IsPresent;
            var note = _bridge.EnsureHealthy(want);
            if (!string.IsNullOrEmpty(note) && note != _lastHealthNote)
            {
                _lastHealthNote = note;
                if (note.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                    note.Contains("zombie", StringComparison.OrdinalIgnoreCase) ||
                    note.Contains("busy", StringComparison.OrdinalIgnoreCase))
                    ShowErrorBalloon(note);
            }

            RefreshUi(note);
        };
        _uiTimer.Start();

        if (_autoItem.Checked && _usb.IsPresent)
            TryStartBridge("USB already present");
        else
            RefreshUi("idle");
    }

    private void OnUsbChanged(bool present)
    {
        if (!_autoItem.Checked)
        {
            RefreshUi(present ? "USB connected (auto off)" : "USB disconnected (auto off)");
            return;
        }

        if (present)
            TryStartBridge("USB connected");
        else
        {
            // Never kill the bridge while SendSpool is in flight — a false USB-absent
            // blip used to Stop() mid-job (TCP already accepted; printer rejected incomplete USB).
            var file = BridgeStatus.TryRead();
            if (string.Equals(file?.State, "printing", StringComparison.OrdinalIgnoreCase))
            {
                RefreshUi("USB blip ignored - job in flight");
                return;
            }

            _bridge.Stop();
            RefreshUi("USB disconnected - bridge stopped");
        }
    }

    private void TryStartBridge(string reason)
    {
        try
        {
            _bridge.Start();
            RefreshUi(reason);
        }
        catch (Exception ex)
        {
            ShowErrorBalloon(ex.Message);
            RefreshUi("error: " + ex.Message);
        }
    }

    private void ShowErrorBalloon(string message)
    {
        _icon.BalloonTipIcon = ToolTipIcon.Error;
        _icon.BalloonTipTitle = "CP-9550 Bridge";
        _icon.BalloonTipText = message.Length > 250 ? message[..250] : message;
        _icon.ShowBalloonTip(5000);
    }

    private void RefreshUi(string? note)
    {
        var usb = _usb.IsPresent;
        var health = _bridge.AssessHealth();
        var file = BridgeStatus.TryRead();
        var dry = _opts.DryRun || (file?.DryRun ?? false);
        var healthy = health.Health == BridgeHealth.Healthy;
        var broken = health.Health is BridgeHealth.ZombieProcess or BridgeHealth.PortOrphan
            || (usb && _autoItem.Checked && health.Health == BridgeHealth.Stopped)
            || (note is not null && note.StartsWith("error:", StringComparison.OrdinalIgnoreCase));

        string state = health.Health switch
        {
            BridgeHealth.ZombieProcess => "BRIDGE ZOMBIE (cleaned/retry)",
            BridgeHealth.PortOrphan => "port busy (no bridge process)",
            BridgeHealth.Healthy when dry => "listening (DRY-RUN)",
            BridgeHealth.Healthy => "listening",
            BridgeHealth.Stopped when !usb => "USB absent",
            BridgeHealth.Stopped => "USB present, bridge stopped",
            _ => "unknown",
        };

        if (!string.IsNullOrEmpty(note) && note.StartsWith("error:", StringComparison.OrdinalIgnoreCase))
            state = "ERROR";

        var tip = $"CP-9550 Bridge - {state}";
        if (health.PrimaryPid is int pid)
            tip += $"\nPID {pid} · 127.0.0.1:{_opts.Port}";
        if (!string.IsNullOrEmpty(note))
            tip += "\n" + note;
        if (tip.Length > 63)
            tip = tip[..63];

        _icon.Text = tip;
        _statusItem.Text = $"Status: {state}" + (note is null ? "" : $" ({note})");

        // Icon: red Error when bridge does not work; yellow Exclamation for dry-run;
        // Warning when USB absent; Information when healthy.
        try
        {
            if (broken || health.Health == BridgeHealth.ZombieProcess || health.Health == BridgeHealth.PortOrphan)
                _icon.Icon = SystemIcons.Error; // red
            else if (!usb)
                _icon.Icon = SystemIcons.Warning;
            else if (healthy && dry)
                _icon.Icon = SystemIcons.Exclamation;
            else if (healthy)
                _icon.Icon = SystemIcons.Information;
            else
                _icon.Icon = SystemIcons.Error; // USB present but not listening
        }
        catch
        {
            /* ignore icon swap failures */
        }
    }

    protected override void ExitThreadCore()
    {
        _uiTimer.Stop();
        _uiTimer.Dispose();
        _usb.Dispose();
        // Stop child bridge on tray exit so 9100 is not orphaned.
        _bridge.Dispose();
        _icon.Visible = false;
        _icon.Dispose();
        base.ExitThreadCore();
    }
}
