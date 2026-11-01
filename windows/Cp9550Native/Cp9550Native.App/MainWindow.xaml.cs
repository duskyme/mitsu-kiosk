using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Cp9550Native.Core;
using Microsoft.Win32;

namespace Cp9550Native.App;

public partial class MainWindow : Window
{
    private string? _photoPath;
    private byte[]? _spool;

    public MainWindow()
    {
        InitializeComponent();
        MediaBox.ItemsSource = MediaCatalog.All.Select(m => $"{m.Label}  ·  {m.Cols}×{m.Rows}  ·  {m.Ribbon}");
        MediaBox.SelectedIndex = 0;
        QualityBox.ItemsSource = new[] { "Fine", "SuperFine", "FineDeep" };
        QualityBox.SelectedIndex = 1;
        FitBox.ItemsSource = new[] { "Cover (orezanie)", "Contain (okraje)" };
        FitBox.SelectedIndex = 0;
        Loaded += async (_, _) => await RefreshPrinterAsync();
    }

    private MediaPreset SelectedMedia() => MediaCatalog.All[Math.Max(0, MediaBox.SelectedIndex)];

    private QualityMode SelectedQuality() => QualityBox.SelectedIndex switch
    {
        0 => QualityMode.Fine,
        2 => QualityMode.FineDeep,
        _ => QualityMode.SuperFine,
    };

    private FitMode SelectedFit() =>
        FitBox.SelectedIndex == 1 ? FitMode.Contain : FitMode.Cover;

    private int SelectedCopies() =>
        int.TryParse(CopiesBox.Text, out var n) ? Math.Clamp(n, 1, 680) : 1;

    private void OnPickPhoto(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Vyber fotografiu",
            Filter = "Obrázky|*.jpg;*.jpeg;*.png;*.tif;*.tiff;*.bmp|Všetky súbory|*.*",
        };
        if (dlg.ShowDialog() == true)
            LoadPhoto(dlg.FileName);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            LoadPhoto(files[0]);
    }

    private void LoadPhoto(string path)
    {
        try
        {
            _photoPath = path;
            FileLabel.Text = path;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            PreviewImage.Source = bmp;
            _spool = null;
            PrintButton.IsEnabled = true;
            StatusText.Text = "Fotka načítaná. Pripravené na tlač / export spoolu.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Chyba načítania", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnRefreshStatus(object sender, RoutedEventArgs e) => await RefreshPrinterAsync();

    private async Task RefreshPrinterAsync()
    {
        PrinterStatus.Text = "Hľadám tlačiareň…";
        try
        {
            var info = await Task.Run(() =>
            {
                if (!PrinterDevice.IsPresent())
                    return null;
                using var prn = PrinterDevice.Open();
                return prn.GetInfo();
            });

            if (info is null)
            {
                PrinterStatus.Text = "Tlačiareň: nepripojená (USB 06D3:03A5). Skontroluj WinUSB.";
                return;
            }

            PrinterStatus.Text =
                $"Tlačiareň: {info.Serial} · {info.MediaName} · {info.MediaRemain}/{info.MediaMax} zostáva";
        }
        catch (Exception ex)
        {
            PrinterStatus.Text = $"Tlačiareň: chyba — {ex.Message}";
        }
    }

    /// <summary>
    /// Snapshot UI-bound options on the dispatcher thread before any Task.Run work.
    /// ComboBox/TextBox must not be read from a background thread.
    /// </summary>
    private (string Path, MediaPreset Media, QualityMode Quality, int Copies, FitMode Fit) CaptureJobArgs()
    {
        if (_photoPath is null)
            throw new InvalidOperationException("Najprv vyber fotografiu.");
        return (_photoPath, SelectedMedia(), SelectedQuality(), SelectedCopies(), SelectedFit());
    }

    private static byte[] BuildSpoolBytes(
        string path, MediaPreset media, QualityMode quality, int copies, FitMode fit) =>
        ImageRasterizer.BuildJob(path, media, quality, copies, fit);

    private async void OnSaveSpool(object sender, RoutedEventArgs e)
    {
        try
        {
            var job = CaptureJobArgs();
            StatusText.Text = "Skladám spool…";
            _spool = await Task.Run(() =>
                BuildSpoolBytes(job.Path, job.Media, job.Quality, job.Copies, job.Fit));
            var dlg = new SaveFileDialog
            {
                Filter = "CP9550S spool|*.mitsu9550s",
                FileName = Path.GetFileNameWithoutExtension(job.Path) + ".mitsu9550s",
            };
            if (dlg.ShowDialog() == true)
            {
                await File.WriteAllBytesAsync(dlg.FileName, _spool);
                StatusText.Text = $"Spool uložený ({_spool.Length:N0} B).";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Export", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = ex.Message;
        }
    }

    private async void OnPrint(object sender, RoutedEventArgs e)
    {
        PrintButton.IsEnabled = false;
        SaveButton.IsEnabled = false;
        Progress.Value = 0;
        try
        {
            var job = CaptureJobArgs();
            StatusText.Text = "Rasterizujem na natívne pixely…";
            _spool = await Task.Run(() =>
                BuildSpoolBytes(job.Path, job.Media, job.Quality, job.Copies, job.Fit));
            StatusText.Text = $"Spool {_spool.Length:N0} B — posielam cez USB…";

            // Progress<T> captures the UI SynchronizationContext when created here.
            var progress = new Progress<double>(p =>
            {
                Progress.Value = p;
                StatusText.Text = $"USB prenos {p:0.0}%";
            });

            var spool = _spool;
            await Task.Run(() =>
            {
                using var prn = PrinterDevice.Open();
                prn.SendSpool(spool, progress);
                prn.WaitIdle();
            });

            Progress.Value = 100;
            StatusText.Text = "Hotovo — tlač dokončená.";
            await RefreshPrinterAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Tlač zlyhala", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = ex.Message;
        }
        finally
        {
            PrintButton.IsEnabled = _photoPath is not null;
            SaveButton.IsEnabled = true;
        }
    }
}
