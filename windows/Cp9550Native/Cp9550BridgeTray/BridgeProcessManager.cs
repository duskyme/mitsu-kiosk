using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

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

    /// <summary>
    /// True when 127.0.0.1:port cannot be bound (something already owns it).
    /// Prefer bind-probe over <see cref="IPGlobalProperties.GetActiveTcpListeners"/> —
    /// on Win11 that API often omits our TcpListener, so the tray kept spawning
    /// second bridges that FATAL with "address already in use".
    /// </summary>
    public bool IsPortListening() => IsPortInUse(_opts.Port);

    public static bool IsPortInUse(int port)
    {
        // Fast path: .NET table (may be incomplete).
        try
        {
            foreach (var ep in IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners())
            {
                if (ep.Port != port)
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
            /* fall through to bind probe */
        }

        // Authoritative: try to bind the same endpoint the bridge uses.
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return false;
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            return true;
        }
        catch
        {
            // Permission / transient — assume busy to avoid colliding.
            return true;
        }
    }

    public static IReadOnlyList<Process> FindBridgeProcesses()
    {
        var list = new List<Process>();
        try
        {
            foreach (var name in new[] { "Cp9550SpoolBridge", "cp9550spoolbridge" })
            {
                foreach (var p in Process.GetProcessesByName(name))
                    list.Add(p);
            }
        }
        catch
        {
            /* ignore */
        }

        return list;
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
            ReapExitedChild();
            if (IsRunning)
                return;

            // Port already owned — adopt an existing SpoolBridge if we can.
            if (IsPortInUse(_opts.Port))
            {
                if (TryAdoptExistingUnlocked())
                    return;
                // Busy but no process we recognize: do not spawn a colliding instance.
                return;
            }

            // Port free but orphan SpoolBridge still running (zombie) — kill then start.
            KillForeignBridgesUnlocked();

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
                WorkingDirectory = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory,
            };
            foreach (var a in args)
                psi.ArgumentList.Add(a);

            _process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start SpoolBridge.");

            // Wait briefly for listen (or instant FATAL exit).
            var deadline = Environment.TickCount64 + 2500;
            while (Environment.TickCount64 < deadline)
            {
                if (_process.HasExited)
                    break;
                if (IsPortInUse(_opts.Port))
                    return;
                Thread.Sleep(100);
            }

            if (_process.HasExited)
            {
                var code = _process.ExitCode;
                _process.Dispose();
                _process = null;
                // Another winner may have bound the port meanwhile — adopt.
                if (IsPortInUse(_opts.Port) && TryAdoptExistingUnlocked())
                    return;
                throw new InvalidOperationException(
                    $"SpoolBridge exited immediately (code {code}). See %ProgramData%\\CP9550\\log\\bridge.log " +
                    "(often: port 9100 already in use).");
            }
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_process is not null)
            {
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

            // Also clear foreign/zombie bridges so the next Start is clean.
            KillForeignBridgesUnlocked();
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

    private void ReapExitedChild()
    {
        if (_process is null)
            return;
        try
        {
            if (_process.HasExited)
            {
                _process.Dispose();
                _process = null;
            }
        }
        catch
        {
            _process = null;
        }
    }

    private bool TryAdoptExistingUnlocked()
    {
        foreach (var p in FindBridgeProcesses())
        {
            try
            {
                if (p.HasExited)
                {
                    p.Dispose();
                    continue;
                }

                _process = p;
                return true;
            }
            catch
            {
                try { p.Dispose(); } catch { /* ignore */ }
            }
        }

        return false;
    }

    private static void KillForeignBridgesUnlocked()
    {
        foreach (var p in FindBridgeProcesses())
        {
            try
            {
                if (!p.HasExited)
                {
                    p.Kill(entireProcessTree: true);
                    p.WaitForExit(3000);
                }
            }
            catch
            {
                /* ignore */
            }
            finally
            {
                try { p.Dispose(); } catch { /* ignore */ }
            }
        }
    }
}
