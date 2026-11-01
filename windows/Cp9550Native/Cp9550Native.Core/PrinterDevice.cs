using LibUsbDotNet;
using LibUsbDotNet.Info;
using LibUsbDotNet.Main;

namespace Cp9550Native.Core;

public sealed class PrinterInfo
{
    public string Serial { get; init; } = "";
    public string MediaName { get; init; } = "";
    public int MediaRemain { get; init; }
    public int MediaMax { get; init; }
}

/// <summary>
/// WinUSB / libusb talker for CP-9550DW-S. Requires the WinUSB INF (or Zadig) once.
/// </summary>
public sealed class PrinterDevice : IDisposable
{
    private readonly UsbDevice _device;
    private readonly UsbEndpointWriter _writer;
    private readonly UsbEndpointReader _reader;
    private readonly byte _outEndpoint;
    private readonly byte _inEndpoint;
    private bool _disposed;

    private PrinterDevice(
        UsbDevice device,
        UsbEndpointWriter writer,
        UsbEndpointReader reader,
        byte outEndpoint,
        byte inEndpoint)
    {
        _device = device;
        _writer = writer;
        _reader = reader;
        _outEndpoint = outEndpoint;
        _inEndpoint = inEndpoint;
    }

    public static bool IsPresent()
    {
        try
        {
            var finder = new UsbDeviceFinder(UsbIds.VendorId, UsbIds.ProductIdCp9550DwS);
            var dev = UsbDevice.OpenUsbDevice(finder);
            if (dev is null)
                return false;
            dev.Close();
            return true;
        }
        catch (FormatException)
        {
            // Bad DeviceInterfaceGUIDs in an older INF (non-hex GUID) surfaces here.
            throw new InvalidOperationException(
                "Neplatný WinUSB GUID v ovládači. Spusti znova windows\\install.ps1 (Admin) " +
                "alebo Zadig → WinUSB, potom odpoj/zapoj USB.");
        }
    }

    public static PrinterDevice Open()
    {
        UsbDevice? device;
        try
        {
            var finder = new UsbDeviceFinder(UsbIds.VendorId, UsbIds.ProductIdCp9550DwS);
            device = UsbDevice.OpenUsbDevice(finder);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(
                "Neplatný WinUSB GUID v ovládači. Spusti znova windows\\install.ps1 (Admin) " +
                "alebo Zadig → WinUSB, potom odpoj/zapoj USB.");
        }

        if (device is null)
            throw new InvalidOperationException(
                "CP-9550DW-S nenájdená (USB 06D3:03A5). Nainštaluj WinUSB INF alebo Zadig.");

        try
        {
            if (device is IUsbDevice whole)
            {
                whole.SetConfiguration(1);
                whole.ClaimInterface(0);
            }

            // CP-9550DW-S exposes bulk OUT 0x01 + bulk IN 0x82 (not 0x81).
            // OpenEndpointReader(Ep01) returns non-null even when that pipe does not
            // exist, so we must pick endpoints from descriptors — same as device.py.
            if (!TryFindBulkEndpoints(device, out var outEp, out var inEp))
            {
                device.Close();
                throw new InvalidOperationException(
                    "Bulk endpointy sa nenašli v USB deskriptoroch CP-9550DW-S.");
            }

            var writer = device.OpenEndpointWriter((WriteEndpointID)outEp, EndpointType.Bulk);
            var reader = device.OpenEndpointReader((ReadEndpointID)inEp, 4096, EndpointType.Bulk);
            if (writer is null || reader is null)
            {
                device.Close();
                throw new InvalidOperationException(
                    $"Bulk endpointy 0x{outEp:X2}/0x{inEp:X2} sa nepodarilo otvoriť.");
            }

            return new PrinterDevice(device, writer, reader, outEp, inEp);
        }
        catch
        {
            try { device.Close(); } catch { /* ignore */ }
            throw;
        }
    }

    /// <summary>
    /// First bulk OUT / bulk IN on interface 0 of the active configuration.
    /// </summary>
    internal static bool TryFindBulkEndpoints(UsbDevice device, out byte outEndpoint, out byte inEndpoint)
    {
        outEndpoint = 0;
        inEndpoint = 0;
        foreach (UsbConfigInfo cfg in device.Configs)
        {
            foreach (UsbInterfaceInfo iface in cfg.InterfaceInfoList)
            {
                foreach (UsbEndpointInfo ep in iface.EndpointInfoList)
                {
                    var d = ep.Descriptor;
                    if ((d.Attributes & 0x03) != 0x02)
                        continue; // not bulk
                    if ((d.EndpointID & 0x80) != 0)
                    {
                        if (inEndpoint == 0)
                            inEndpoint = d.EndpointID;
                    }
                    else if (outEndpoint == 0)
                    {
                        outEndpoint = d.EndpointID;
                    }
                }
            }
        }

        return outEndpoint != 0 && inEndpoint != 0;
    }

    private static string UsbErrorDetail(ErrorCode ec) =>
        ec == ErrorCode.None
            ? ""
            : $" ({ec}, Win32/LibUsb: {UsbDevice.LastErrorNumber} {UsbDevice.LastErrorString})";

    public void Write(byte[] data, int timeoutMs = 15000)
    {
        var ec = _writer.Write(data, timeoutMs, out var transferred);
        if (ec != ErrorCode.None || transferred != data.Length)
            throw new IOException(
                $"USB write failed on EP 0x{_outEndpoint:X2}: {ec} " +
                $"({transferred}/{data.Length}){UsbErrorDetail(ec)}");
    }

    public byte[] Read(int size = 128, int timeoutMs = 5000)
    {
        var buf = new byte[size];
        var ec = _reader.Read(buf, timeoutMs, out var transferred);
        if (ec != ErrorCode.None && ec != ErrorCode.IoTimedOut)
            throw new IOException(
                $"USB read failed on EP 0x{_inEndpoint:X2}: {ec}{UsbErrorDetail(ec)}");
        if (transferred <= 0)
            return Array.Empty<byte>();
        if (transferred == buf.Length)
            return buf;
        var slice = new byte[transferred];
        Buffer.BlockCopy(buf, 0, slice, 0, transferred);
        return slice;
    }

    public byte[] Query(byte[] cmd, int size = 64)
    {
        FlushInput();
        Write(cmd);
        return Read(size);
    }

    /// <summary>
    /// Drop stale IN packets (e.g. a late serial reply) so the next command
    /// response is not mistaken for media/status.
    /// </summary>
    private void FlushInput(int timeoutMs = 50, int maxPackets = 16)
    {
        var buf = new byte[512];
        for (var i = 0; i < maxPackets; i++)
        {
            var ec = _reader.Read(buf, timeoutMs, out var transferred);
            if (ec == ErrorCode.IoTimedOut || transferred <= 0)
                return;
        }
    }

    /// <summary>
    /// Write <paramref name="cmd"/> then read until <paramref name="accept"/>
    /// matches, skipping leftover packets from earlier commands.
    /// </summary>
    private byte[] QueryUntil(byte[] cmd, Func<byte[], bool> accept, int size = 64, int maxReads = 8)
    {
        FlushInput();
        Write(cmd);
        byte[] last = Array.Empty<byte>();
        for (var i = 0; i < maxReads; i++)
        {
            var timeout = i == 0 ? 5000 : 400;
            last = Read(size, timeout);
            if (last.Length == 0)
                continue;
            if (accept(last))
                return last;
            // Stale/unexpected packet — keep reading for the real reply.
        }

        return last;
    }

    public void HandshakeS()
    {
        FlushInput();
        Write(Protocol.InitCmd1);
        Write(Protocol.InitCmd2);
        _ = Read(16);
    }

    public MediaStatus MediaStatus()
    {
        var raw = QueryUntil(
            Protocol.CmdMedia,
            b => Protocol.ParseMediaStatus(b) is not null,
            size: 64);
        return Protocol.ParseMediaStatus(raw)
            ?? throw new InvalidOperationException(
                "Neočakávaná media odpoveď: " + Protocol.DescribeUnexpectedReply(raw));
    }

    public byte[] StatusRaw() => QueryUntil(
        Protocol.CmdStatus,
        b => Protocol.ParsePrinterStatus(b) is not null,
        size: 64);

    public PrinterStatus Status()
    {
        var raw = StatusRaw();
        return Protocol.ParsePrinterStatus(raw)
            ?? throw new InvalidOperationException(
                "Neočakávaná status odpoveď: " + Protocol.DescribeUnexpectedReply(raw));
    }

    public byte[] StatusB() => Query(Protocol.CmdStatusB, 64);

    public string ReadSerial()
    {
        var raw = QueryUntil(
            Protocol.CmdSerial,
            b => Protocol.LooksLikeSerial(b),
            size: 128);
        return Protocol.ParseSerial(raw) ?? "";
    }

    public PrinterInfo GetInfo()
    {
        var media = MediaStatus();
        return new PrinterInfo
        {
            Serial = ReadSerial(),
            MediaName = media.Name,
            MediaRemain = media.Remain,
            MediaMax = media.Max,
        };
    }

    /// <summary>Wait until sts5==0 (printer will accept a new job).</summary>
    public void WaitReadyForJob(TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        PrinterStatus? last = null;
        while (DateTime.UtcNow < deadline)
        {
            last = Status();
            if (last.ReadyForJob)
                return;
            Thread.Sleep(500);
        }
        throw new TimeoutException(
            "Tlačiareň nie je pripravená na job (sts5≠0). " + (last?.Summary ?? ""));
    }

    /// <summary>Wait until sts1==0 (print finished / idle). Used after footer.</summary>
    public void WaitPrintComplete(TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromMinutes(5));
        PrinterStatus? last = null;
        while (DateTime.UtcNow < deadline)
        {
            last = Status();
            if (last.IsIdle)
                return;
            Thread.Sleep(1000);
        }
        throw new TimeoutException(
            "Tlačiareň nedokončila tlač (sts1≠0). " + (last?.Summary ?? ""));
    }

    /// <summary>Back-compat alias: ready-for-job wait (sts5), not print-complete.</summary>
    public void WaitIdle(TimeSpan? timeout = null) => WaitReadyForJob(timeout);

    public void SendSpool(byte[] spool, IProgress<double>? progress = null)
    {
        if (spool.Length < 154)
            throw new ArgumentException("Spool je príliš malý.");
        if (spool[0] != 0x1B || spool[1] != 0x57 || spool[2] != 0x20 || spool[3] != 0x2E)
            throw new ArgumentException("Toto nie je CP-9550 header-1 spool.");

        var headers = spool.AsSpan(0, 150);
        var body = spool.AsSpan(150);
        ReadOnlySpan<byte> planes;
        if (body.Length >= 4 &&
            body[^4] == Protocol.Footer9550S[0] &&
            body[^3] == Protocol.Footer9550S[1] &&
            body[^2] == Protocol.Footer9550S[2] &&
            body[^1] == Protocol.Footer9550S[3])
        {
            planes = body[..^4];
        }
        else
        {
            planes = body;
        }

        // selphy_print CP-9550DW-S sequence:
        // handshake → media/statusB/status(ready) → 1B 44 00 00 → status again →
        // hdr1+hdr2+hdr3 → clear-memory → planes → media/statusB/status(check) → footer.
        HandshakeS();
        _ = MediaStatus();
        _ = StatusB();
        WaitReadyForJob(TimeSpan.FromSeconds(30));

        Write(Protocol.StartJobS);
        var pre = Status();
        if (!pre.ReadyForJob)
            throw new InvalidOperationException(
                "Po StartJobS tlačiareň stále nie je ready: " + pre.Summary);

        // Three 50-byte headers as separate OUT transfers (matches selphy_print).
        Write(headers[..50].ToArray());
        Write(headers[50..100].ToArray());
        Write(headers[100..150].ToArray());
        Write(Protocol.ClearMemoryS);

        const int chunk = 262144;
        var sent = 0;
        while (sent < planes.Length)
        {
            var n = Math.Min(chunk, planes.Length - sent);
            var piece = planes.Slice(sent, n).ToArray();
            Write(piece, timeoutMs: 60000);
            sent += n;
            progress?.Report(100.0 * sent / planes.Length);
        }

        _ = MediaStatus();
        _ = StatusB();
        var sts = Status();
        // selphy: require sts5==0 and sts3 bits 0xC0 before "end data" footer.
        if (!sts.ReadyForJob)
            throw new InvalidOperationException(
                "Pred footerom nie je sts5 ready — tlačiareň pravdepodobne neprijala dáta. " +
                sts.Summary);
        if ((sts.Sts3 & 0xC0) == 0)
            throw new InvalidOperationException(
                "Pred footerom neočakávaný sts3 (chýbajú bity 0xC0) — " + sts.Summary);

        Write(Protocol.Footer9550S);
        progress?.Report(100);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try
        {
            if (_device is IUsbDevice whole)
                whole.ReleaseInterface(0);
        }
        catch
        {
            // ignore
        }

        try
        {
            _device.Close();
        }
        catch
        {
            // ignore
        }
    }
}
