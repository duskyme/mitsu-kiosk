using Cp9550Native.Core;

namespace Cp9550BridgeTray;

/// <summary>
/// Polls WinUSB presence for VID 06D3 / PID 03A5.
/// Retail CPD9550V (PID 03A1) used usbprint + a status monitor; with WinUSB we cannot
/// rely on that stack, so the tray owns plug/unplug → bridge start/stop.
/// </summary>
internal sealed class UsbPresenceMonitor : IDisposable
{
    /// <summary>~4.5s at 1500ms — ignore transient Open/enum blips during SendSpool.</summary>
    private const int DisconnectConfirmPolls = 3;

    private readonly System.Windows.Forms.Timer _timer;
    private bool? _last;
    private int _absentPolls;
    private bool _disposed;

    public event Action<bool>? PresenceChanged;

    public bool IsPresent { get; private set; }

    public UsbPresenceMonitor(int intervalMs = 1500)
    {
        _timer = new System.Windows.Forms.Timer { Interval = intervalMs };
        _timer.Tick += (_, _) => Poll();
    }

    public void Start()
    {
        Poll();
        _timer.Start();
    }

    public void Poll()
    {
        bool raw;
        try
        {
            raw = PrinterDevice.IsPresent();
        }
        catch
        {
            raw = false;
        }

        bool present;
        if (raw)
        {
            _absentPolls = 0;
            present = true;
        }
        else
        {
            _absentPolls++;
            // Stay "present" until several consecutive misses — a single false during
            // an in-flight WinUSB claim must not Stop() the bridge.
            present = _last == true && _absentPolls < DisconnectConfirmPolls;
            if (_absentPolls >= DisconnectConfirmPolls)
                present = false;
        }

        IsPresent = present;
        if (_last != present)
        {
            _last = present;
            PresenceChanged?.Invoke(present);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _timer.Stop();
        _timer.Dispose();
    }
}
