using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;

namespace Cp9550BridgeTray;

/// <summary>Owns the Cp9550SpoolBridge child process (no UAC — user-mode WinUSB).</summary>
internal sealed class BridgeProcessManager : IDisposable
{
    private readonly TrayOptions _opts;
    private readonly object _gate = new();
    private Process? _process;
    private bool _disposed;

    public BridgeProcessManager(TrayOptions opts) => _opts = opts;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                try
                {
                    return _process is { HasExited: false };
                }
                catch
                {
                    return false;
                }
            }
        }
    }

    public int? ProcessId
    {
        get
        {
            lock (_gate)
            {
                try
                {
                    return _process is { HasExited: false } ? _process.Id : null;
                }
                catch
                {
                    return null;
                }
            }
        }
    }

    public bool IsPortListening()
    {
        // Do NOT TcpClient.Connect — that Accept()s on the bridge and looks like an empty job.
        try
        {
            var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
            foreach (var ep in listeners)
            {
                if (ep.Port != _opts.Port)
                    continue;
                if (IPAddress.IsLoopback(ep.Address) ||
                    ep.Address.Equals(IPAddress.Any) ||
                    ep.Address.Equals(IPAddress.IPv6Any) ||
                    ep.Address.Equals(IPAddress.IPv6Loopback))
                    return true;
            }
        }
        catch
        {
            /* fall through */
        }

        return false;
    }

    public string ResolveBridgeExe()
    {
        if (!string.IsNullOrWhiteSpace(_opts.BridgeExe) && File.Exists(_opts.BridgeExe))
            return _opts.BridgeExe;

        var here = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(here, "Cp9550SpoolBridge.exe"),
            Path.GetFullPath(Path.Combine(here, "..", "..", "..", "..", "Cp9550SpoolBridge", "bin", "Release", "net8.0-windows", "Cp9550SpoolBridge.exe")),
            Path.GetFullPath(Path.Combine(here, "..", "..", "..", "..", "Cp9550SpoolBridge", "bin", "Debug", "net8.0-windows", "Cp9550SpoolBridge.exe")),
            Path.GetFullPath(Path.Combine(here, "..", "Cp9550SpoolBridge", "Cp9550SpoolBridge.exe")),
        };
        foreach (var c in candidates)
        {
            if (File.Exists(c))
                return c;
        }

        throw new FileNotFoundException(
            "Cp9550SpoolBridge.exe not found. Build the bridge project or pass --bridge-exe.");
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            if (IsRunning)
                return;

            // Another instance may already own 9100 (manual start-bridge.ps1).
            if (IsPortListening())
                return;

            var exe = ResolveBridgeExe();
            var args = new List<string> { "--port", _opts.Port.ToString(), "--status-file", BridgeStatus.DefaultPath };
            if (_opts.DryRun)
                args.Add("--dry-run");
            if (!string.IsNullOrWhiteSpace(_opts.SaveDir))
            {
                args.Add("--save-dir");
                args.Add(_opts.SaveDir!);
            }

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? hereSafe(),
            };
            foreach (var a in args)
                psi.ArgumentList.Add(a);

            _process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start SpoolBridge.");
        }

        static string hereSafe() => AppContext.BaseDirectory;
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_process is null)
                return;
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit(3000);
                }
            }
            catch
            {
                /* ignore */
            }
            finally
            {
                _process.Dispose();
                _process = null;
            }
        }

        try
        {
            if (File.Exists(BridgeStatus.DefaultPath))
                File.Delete(BridgeStatus.DefaultPath);
        }
        catch
        {
            /* ignore */
        }
    }

    public void Restart()
    {
        Stop();
        Thread.Sleep(400);
        Start();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Stop();
    }
}
