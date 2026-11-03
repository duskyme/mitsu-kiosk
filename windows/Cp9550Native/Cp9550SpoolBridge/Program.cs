using System.Net;
using System.Net.Sockets;
using Cp9550Native.Core;

namespace Cp9550SpoolBridge;

internal static class Program
{
    /// <summary>
    /// Default receive cap: enough for 4×6 SuperFine (~9.1 MB) with headroom;
    /// raises with <c>--max-receive-bytes</c> when 5×7/6×9 jobs are wired.
    /// </summary>
    internal const int DefaultMaxReceiveBytes = 16 * 1024 * 1024;

    private static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FATAL: " + ex.Message);
            return 2;
        }
    }

    private static int Run(string[] args)
    {
        var opts = Options.Parse(args);
        if (opts.Help)
        {
            Options.PrintHelp();
            return 0;
        }

        if (opts.SpoolFile is not null)
            return SendOneShot(opts);

        return Listen(opts);
    }

    private static int SendOneShot(Options opts)
    {
        var info = new FileInfo(opts.SpoolFile!);
        if (!info.Exists)
        {
            Console.Error.WriteLine("Spool file not found: " + opts.SpoolFile);
            return 1;
        }

        if (info.Length > opts.MaxReceiveBytes)
        {
            Console.Error.WriteLine(
                $"Spool file {info.Length} B exceeds --max-receive-bytes {opts.MaxReceiveBytes}.");
            return 1;
        }

        var spool = File.ReadAllBytes(opts.SpoolFile!);
        Console.WriteLine($"One-shot spool {opts.SpoolFile} ({spool.Length} bytes)");
        return DispatchSpool(spool, opts) ? 0 : 1;
    }

    private static int Listen(Options opts)
    {
        var listener = new TcpListener(IPAddress.Loopback, opts.Port);
        listener.Start();
        Console.WriteLine(
            $"CP-9550 spool bridge listening on 127.0.0.1:{opts.Port} " +
            $"(dryRun={opts.DryRun}, saveDir={opts.SaveDir ?? "-"}, " +
            $"maxReceive={opts.MaxReceiveBytes} B)");
        if (opts.DryRun)
        {
            Console.WriteLine(
                "*** DRY-RUN: TCP accepts + validates/saves only. " +
                "Windows will report print SUCCESS; the printer will NOT move. ***");
            Console.WriteLine(
                "*** Restart without --dry-run (see windows\\v4-driver\\start-bridge.ps1). ***");
        }
        Console.WriteLine("Point a Standard TCP/IP Port (RAW) at this address.");
        Console.WriteLine("Single-flight: busy connections are rejected (no buffered backlog).");
        Console.WriteLine("Ctrl+C to stop.");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
            try { listener.Stop(); } catch { /* ignore */ }
        };

        // Gate accept/receive + DispatchSpool together so concurrent clients cannot
        // each CopyTo a huge buffer before the previous job finishes.
        using var jobGate = new SemaphoreSlim(1, 1);
        while (!cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = listener.AcceptTcpClient();
            }
            catch (SocketException) when (cts.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (cts.IsCancellationRequested)
            {
                break;
            }

            _ = Task.Run(() =>
            {
                using (client)
                {
                    try
                    {
                        HandleClient(client, opts, jobGate);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[{Stamp()}] client error: {ex.Message}");
                    }
                }
            });
        }

        Console.WriteLine("Bridge stopped.");
        return 0;
    }

    private static void HandleClient(TcpClient client, Options opts, SemaphoreSlim jobGate)
    {
        client.NoDelay = true;
        client.ReceiveTimeout = opts.ReceiveTimeoutMs;
        var remote = client.Client.RemoteEndPoint?.ToString() ?? "?";
        Console.WriteLine($"[{Stamp()}] accept {remote}");

        // Non-blocking: if a job (including its receive) is in flight, drop this client
        // instead of buffering another full spool in memory.
        if (!jobGate.Wait(0))
        {
            Console.Error.WriteLine($"[{Stamp()}] busy — rejecting {remote} (single-flight)");
            return;
        }

        try
        {
            using var stream = client.GetStream();
            byte[] spool;
            try
            {
                spool = ReadCapped(stream, opts.MaxReceiveBytes);
            }
            catch (InvalidDataException ex)
            {
                Console.Error.WriteLine($"[{Stamp()}] receive rejected from {remote}: {ex.Message}");
                return;
            }

            Console.WriteLine($"[{Stamp()}] received {spool.Length} bytes from {remote}");
            if (!DispatchSpool(spool, opts))
                Console.Error.WriteLine($"[{Stamp()}] job failed");
        }
        finally
        {
            jobGate.Release();
        }
    }

    /// <summary>Read until EOF, failing if more than <paramref name="maxBytes"/> arrive.</summary>
    internal static byte[] ReadCapped(Stream stream, int maxBytes)
    {
        if (maxBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBytes));

        using var ms = new MemoryStream(Math.Min(maxBytes, 256 * 1024));
        var buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            var n = stream.Read(buffer, 0, buffer.Length);
            if (n <= 0)
                break;
            total += n;
            if (total > maxBytes)
            {
                throw new InvalidDataException(
                    $"Spool exceeds max receive size ({maxBytes} B). " +
                    "Raise --max-receive-bytes only for known larger media.");
            }

            ms.Write(buffer, 0, n);
        }

        return ms.ToArray();
    }

    private static bool DispatchSpool(byte[] spool, Options opts)
    {
        if (spool.Length == 0)
        {
            Console.Error.WriteLine("Empty spool — ignored.");
            return false;
        }

        try
        {
            ValidateCp9550Header(spool);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Spool validation failed: " + ex.Message);
            if (opts.SaveDir is not null)
                SaveSpool(opts.SaveDir, spool, "invalid");
            if (!opts.AllowNonNative)
                return false;
            Console.WriteLine(
                "Continuing because --allow-non-native is set " +
                "(spike capture only — SendSpool still requires a CP-9550 header).");
        }

        if (opts.SaveDir is not null)
            SaveSpool(opts.SaveDir, spool, "ok");

        if (opts.DryRun)
        {
            Console.WriteLine(
                $"Dry-run: validated/saved spool ({spool.Length} B). SendSpool skipped.");
            return true;
        }

        if (!PrinterDevice.IsPresent())
        {
            Console.Error.WriteLine(
                "CP-9550DW-S not present (USB 06D3:03A5). Use --dry-run or connect printer.");
            return false;
        }

        Console.WriteLine("Opening WinUSB and calling PrinterDevice.SendSpool…");
        var progress = new Progress<double>(p =>
        {
            if (p is >= 100 or 0 or 25 or 50 or 75)
                Console.WriteLine($"  progress {p:0.#}%");
        });

        try
        {
            using var prn = PrinterDevice.Open();
            prn.SendSpool(spool, progress);
            Console.WriteLine("SendSpool complete.");
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("SendSpool failed: " + ex.Message);
            return false;
        }
    }

    /// <summary>Same gate as <see cref="PrinterDevice.SendSpool"/>.</summary>
    internal static void ValidateCp9550Header(byte[] spool)
    {
        if (spool.Length < 154)
            throw new ArgumentException($"Spool too small ({spool.Length} B).");
        if (spool[0] != 0x1B || spool[1] != 0x57 || spool[2] != 0x20 || spool[3] != 0x2E)
            throw new ArgumentException("Not a CP-9550 header-1 spool (expected 1B 57 20 2E).");
    }

    private static void SaveSpool(string dir, byte[] spool, string tag)
    {
        Directory.CreateDirectory(dir);
        var name = $"spool-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{tag}.bin";
        var path = Path.Combine(dir, name);
        File.WriteAllBytes(path, spool);
        Console.WriteLine($"Saved {path}");
    }

    private static string Stamp() => DateTime.Now.ToString("HH:mm:ss");
}

internal sealed class Options
{
    public int Port { get; init; } = 9100;
    public bool DryRun { get; init; }
    public bool Help { get; init; }
    public bool AllowNonNative { get; init; }
    public string? SpoolFile { get; init; }
    public string? SaveDir { get; init; }
    public int ReceiveTimeoutMs { get; init; } = 120_000;
    public int MaxReceiveBytes { get; init; } = Program.DefaultMaxReceiveBytes;

    public static Options Parse(string[] args)
    {
        var port = 9100;
        var dry = false;
        var help = false;
        var allowNonNative = false;
        string? spool = null;
        string? save = null;
        var timeout = 120_000;
        var maxRecv = Program.DefaultMaxReceiveBytes;

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            switch (a)
            {
                case "-h" or "--help" or "/?":
                    help = true;
                    break;
                case "--dry-run":
                    dry = true;
                    break;
                case "--allow-non-native":
                    allowNonNative = true;
                    break;
                case "--port":
                    port = int.Parse(args[++i]);
                    break;
                case "--spool-file":
                    spool = args[++i];
                    break;
                case "--save-dir":
                    save = args[++i];
                    break;
                case "--receive-timeout-ms":
                    timeout = int.Parse(args[++i]);
                    break;
                case "--max-receive-bytes":
                    maxRecv = int.Parse(args[++i]);
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {a}");
            }
        }

        return new Options
        {
            Port = port,
            DryRun = dry,
            Help = help,
            AllowNonNative = allowNonNative,
            SpoolFile = spool,
            SaveDir = save,
            ReceiveTimeoutMs = timeout,
            MaxReceiveBytes = maxRecv,
        };
    }

    public static void PrintHelp()
    {
        Console.WriteLine($"""
            Cp9550SpoolBridge — localhost RAW TCP → PrinterDevice.SendSpool

            Usage:
              Cp9550SpoolBridge [--port 9100] [--dry-run] [--save-dir DIR]
              Cp9550SpoolBridge --spool-file JOB.bin [--dry-run]

            Options:
              --port N                 Listen on 127.0.0.1:N (default 9100)
              --dry-run                Validate/save only; do not open USB
              --spool-file PATH        One-shot: send existing spool via SendSpool
              --save-dir DIR           Write each received spool to DIR
              --allow-non-native       Keep non-header payloads when saving/capturing
                                       (does not make SendSpool accept XPS)
              --max-receive-bytes N    Cap TCP/file spool size (default {Program.DefaultMaxReceiveBytes})
              --receive-timeout-ms N   Per-connection read timeout (default 120000)
              -h, --help               This help

            Single-flight: only one receive+SendSpool at a time; extra connections
            are rejected immediately (no unbounded in-memory backlog).

            Install story: add a Standard TCP/IP Port → 127.0.0.1:9100 (RAW),
            then install the V4 queue stubs under windows/v4-driver/.
            """);
    }
}
