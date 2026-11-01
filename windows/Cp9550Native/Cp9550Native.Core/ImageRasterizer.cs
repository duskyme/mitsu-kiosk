using SkiaSharp;

namespace Cp9550Native.Core;

public static class ImageRasterizer
{
    /// <summary>
    /// Rasterize to exact printer pixels as tightly packed RGB (no ICC / no Gutenprint LUT).
    /// </summary>
    public static byte[] RasterizeToRgb(string path, MediaPreset media, FitMode fit = FitMode.Cover)
    {
        using var input = SKBitmap.Decode(path)
            ?? throw new InvalidOperationException($"Nepodarilo sa načítať obrázok: {path}");

        var scaleX = media.Cols / (float)input.Width;
        var scaleY = media.Rows / (float)input.Height;
        var scale = fit == FitMode.Cover ? Math.Max(scaleX, scaleY) : Math.Min(scaleX, scaleY);
        var newW = Math.Max(1, (int)Math.Round(input.Width * scale));
        var newH = Math.Max(1, (int)Math.Round(input.Height * scale));

        using var resized = input.Resize(new SKImageInfo(newW, newH), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
            ?? throw new InvalidOperationException("Resize zlyhal.");

        using var canvasBmp = new SKBitmap(media.Cols, media.Rows, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(canvasBmp))
        {
            canvas.Clear(SKColors.White);
            var dx = (media.Cols - newW) / 2f;
            var dy = (media.Rows - newH) / 2f;
            canvas.DrawBitmap(resized, dx, dy);
        }

        var rgb = new byte[media.Cols * media.Rows * 3];
        var pixels = canvasBmp.Pixels;
        for (var i = 0; i < pixels.Length; i++)
        {
            var c = pixels[i];
            var o = i * 3;
            rgb[o] = c.Red;
            rgb[o + 1] = c.Green;
            rgb[o + 2] = c.Blue;
        }
        return rgb;
    }

    public static byte[] BuildJob(
        string imagePath,
        MediaPreset media,
        QualityMode quality = QualityMode.SuperFine,
        int copies = 1,
        FitMode fit = FitMode.Cover)
    {
        var rgb = RasterizeToRgb(imagePath, media, fit);
        return Protocol.BuildSpool(rgb, media, quality, copies);
    }
}
