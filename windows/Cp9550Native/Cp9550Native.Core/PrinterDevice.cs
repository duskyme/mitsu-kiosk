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
        Write(cmd);
        return Read(size);
    }

    public void HandshakeS()
    {
        Write(Protocol.InitCmd1);
        Write(Protocol.InitCmd2);
        _ = Read(16);
    }

    public MediaStatus MediaStatus()
    {
        var raw = Query(Protocol.CmdMedia, 64);
        return Protocol.ParseMediaStatus(raw)
            ?? throw new InvalidOperationException(
                $"Neočakávaná media odpoveď: {Convert.ToHexString(raw.AsSpan(0, Math.Min(16, raw.Length)))}");
    }

    public byte[] Status() => Query(Protocol.CmdStatus, 64);

    public byte[] StatusB() => Query(Protocol.CmdStatusB, 64);

    public string ReadSerial()
    {
        var raw = Query(Protocol.CmdSerial, 128);
        if (raw.Length < 8)
            return "";
        var length = raw[4];
        if (5 + length > raw.Length)
            return "";
        try
        {
            return System.Text.Encoding.Unicode.GetString(raw, 5, length).Trim();
        }
        catch
        {
            return Convert.ToHexString(raw);
        }
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

    public void WaitIdle(TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromMinutes(3));
        while (DateTime.UtcNow < deadline)
        {
            var sts = Status();
            var sts5 = sts.Length > 16 ? sts[16] : (byte)0xFF;
            if (sts5 == 0)
                return;
            Thread.Sleep(1000);
        }
        throw new TimeoutException("Tlačiareň sa nevrátila do idle.");
    }

    public void SendSpool(byte[] spool, IProgress<double>? progress = null)
    {
        if (spool.Length < 154)
            throw new ArgumentException("Spool je príliš malý.");
        if (spool[0] != 0x1B || spool[1] != 0x57 || spool[2] != 0x20 || spool[3] != 0x2E)
            throw new ArgumentException("Toto nie je CP-9550 header-1 spool.");

        var headers = spool.AsSpan(0, 150).ToArray();
        var body = spool.AsSpan(150);
        byte[] planes;
        if (body.Length >= 4 &&
            body[^4] == Protocol.Footer9550S[0] &&
            body[^3] == Protocol.Footer9550S[1] &&
            body[^2] == Protocol.Footer9550S[2] &&
            body[^1] == Protocol.Footer9550S[3])
        {
            planes = body[..^4].ToArray();
        }
        else
        {
            planes = body.ToArray();
        }

        HandshakeS();
        _ = MediaStatus();
        _ = StatusB();
        WaitIdle(TimeSpan.FromSeconds(30));

        Write(headers);
        Write(Protocol.ClearMemoryS);

        const int chunk = 262144;
        var sent = 0;
        while (sent < planes.Length)
        {
            var n = Math.Min(chunk, planes.Length - sent);
            var piece = planes.AsSpan(sent, n).ToArray();
            Write(piece, timeoutMs: 60000);
            sent += n;
            progress?.Report(100.0 * sent / planes.Length);
        }

        _ = MediaStatus();
        _ = StatusB();
        var sts = Status();
        if (sts.Length > 6 && sts[6] != 0)
            throw new InvalidOperationException("Kryt tlačiarne je otvorený (alebo status MM error).");

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
