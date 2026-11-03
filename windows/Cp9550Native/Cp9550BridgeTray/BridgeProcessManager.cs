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

    /// <summary>True when a live SpoolBridge owns the listen port (not a zombie).</summary>
    public bool IsHealthy()
    {
        lock (_gate)
            return AssessHealthUnlocked().Health == BridgeHealth.Healthy;
    }

    public BridgeHealthReport AssessHealth()
    {
        lock (_gate)
            return AssessHealthUnlocked();
    }

    /// <summary>
    /// Kill SpoolBridge processes that are alive but not holding the listen port,
    /// and clear a stale status file. Returns how many processes were killed.
    /// </summary>
    public int CleanupZombies()
    {
        lock (_gate)
            return CleanupZombiesUnlocked();
    }

    /// <summary>
    /// Periodic maintenance: reap zombies; if USB path wants a bridge and health
    /// is bad after cleanup, start a fresh one. Returns a short note or null.
    /// </summary>
    public string? EnsureHealthy(bool wantRunning)
    {
        lock (_gate)
        {
            if (_disposed)
                return null;

            ReapExitedChild();
            var before = AssessHealthUnlocked();
            string? note = null;

            if (before.Health == BridgeHealth.ZombieProcess)
            {
                var n = CleanupZombiesUnlocked();
                note = n > 0 ? $"cleaned {n} zombie bridge(s)" : "zombie cleared";
            }

            var after = AssessHealthUnlocked();
            if (!wantRunning)
                return note;

            if (after.Health == BridgeHealth.Healthy)
            {
                if (_process is null || _process.HasExited)
                    TryAdoptExistingUnlocked();
                return note;
            }

            // Port orphan (busy, no process) — cannot safely steal; surface as unhealthy.
            if (after.Health == BridgeHealth.PortOrphan)
                return note ?? "port 9100 busy (no SpoolBridge process)";

            // Stopped or still zombie after cleanup — start fresh (outside nested Start lock: same lock).
            try
            {
                StartUnlocked();
                return note is null ? "bridge started" : note + "; bridge restarted";
            }
            catch (Exception ex)
            {
                return (note is null ? "" : note + "; ") + "start failed: " + ex.Message;
            }
        }
    }

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
            StartUnlocked();
    }

    private void StartUnlocked()
    {
        if (_disposed)
            return;
        ReapExitedChild();
        if (IsRunning && IsPortInUse(_opts.Port))
            return;

        // Owned process alive but not listening = zombie child — drop it.
        if (IsRunning && !IsPortInUse(_opts.Port))
        {
            try
            {
                if (_process is { HasExited: false })
                {
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit(3000);
                }
            }
            catch { /* ignore */ }
            finally
            {
                _process?.Dispose();
                _process = null;
            }
        }

        var health = AssessHealthUnlocked();
        if (health.Health == BridgeHealth.ZombieProcess)
            CleanupZombiesUnlocked();

        // Port already owned — adopt an existing SpoolBridge if we can.
        if (IsPortInUse(_opts.Port))
        {
            if (TryAdoptExistingUnlocked())
                return;
            // Busy but no process we recognize: do not spawn a colliding instance.
            throw new InvalidOperationException(
                $"Port {_opts.Port} is in use but no Cp9550SpoolBridge process was found.");
        }

        // Port free — kill any remaining SpoolBridge leftovers, then start.
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
            if (IsPortInUse(_opts.Port) && TryAdoptExistingUnlocked())
                return;
            throw new InvalidOperationException(
                $"SpoolBridge exited immediately (code {code}). See %ProgramData%\\CP9550\\log\\bridge.log " +
                "(often: port 9100 already in use).");
        }

        // Still running but port never bound — treat as zombie and fail loudly.
        CleanupZombiesUnlocked();
        throw new InvalidOperationException(
            $"SpoolBridge started but never listened on 127.0.0.1:{_opts.Port} (zombie cleaned).");
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

    private BridgeHealthReport AssessHealthUnlocked()
    {
        var portInUse = IsPortInUse(_opts.Port);
        var alive = new List<Process>();
        foreach (var p in FindBridgeProcesses())
        {
            try
            {
                if (!p.HasExited)
                    alive.Add(p);
                else
                    p.Dispose();
            }
            catch
            {
                try { p.Dispose(); } catch { /* ignore */ }
            }
        }

        int? pid = alive.Count > 0 ? alive[0].Id : null;
        // Dispose extras we are not adopting here (Assess is read-only aside from disposing exited).
        for (var i = 1; i < alive.Count; i++)
        {
            try { alive[i].Dispose(); } catch { /* ignore */ }
        }

        if (alive.Count == 0)
        {
            return new BridgeHealthReport(
                portInUse ? BridgeHealth.PortOrphan : BridgeHealth.Stopped,
                0,
                portInUse,
                null);
        }

        // Keep first Process handle only if we might adopt later — dispose first too for Assess.
        try { alive[0].Dispose(); } catch { /* ignore */ }

        if (!portInUse)
            return new BridgeHealthReport(BridgeHealth.ZombieProcess, alive.Count, false, pid);

        return new BridgeHealthReport(BridgeHealth.Healthy, alive.Count, true, pid);
    }

    private int CleanupZombiesUnlocked()
    {
        var portInUse = IsPortInUse(_opts.Port);
        var killed = 0;

        // If port is free, every SpoolBridge is a zombie/orphan → kill all.
        // If port is busy, only kill processes when we later Restart; do not kill the listener.
        if (portInUse)
            return 0;

        foreach (var p in FindBridgeProcesses())
        {
            try
            {
                if (!p.HasExited)
                {
                    p.Kill(entireProcessTree: true);
                    p.WaitForExit(3000);
                    killed++;
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

        if (_process is not null)
        {
            try { _process.Dispose(); } catch { /* ignore */ }
            _process = null;
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

        return killed;
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

                // Only adopt if the listen port is actually held (not a zombie).
                if (!IsPortInUse(_opts.Port))
                {
                    try
                    {
                        p.Kill(entireProcessTree: true);
                        p.WaitForExit(3000);
                    }
                    catch { /* ignore */ }
                    finally
                    {
                        try { p.Dispose(); } catch { /* ignore */ }
                    }

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

    private void KillForeignBridgesUnlocked()
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

        if (_process is not null)
        {
            try { _process.Dispose(); } catch { /* ignore */ }
            _process = null;
        }
    }
}
