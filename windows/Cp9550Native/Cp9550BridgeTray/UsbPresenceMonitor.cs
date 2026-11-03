using Cp9550Native.Core;

namespace Cp9550BridgeTray;

/// <summary>
/// Polls WinUSB presence for VID 06D3 / PID 03A5.
/// Retail CPD9550V (PID 03A1) used usbprint + a status monitor; with WinUSB we cannot
/// rely on that stack, so the tray owns plug/unplug → bridge start/stop.
/// </summary>
internal sealed class UsbPresenceMonitor : IDisposable
{
    private readonly System.Windows.Forms.Timer _timer;
    private bool? _last;
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
        bool present;
        try
        {
            present = PrinterDevice.IsPresent();
        }
        catch
        {
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
