using Hivox.Mobile.Services;
using Hivox.Mobile.ViewModels;
using ZXing.Net.Maui;

namespace Hivox.Mobile.Views;

public partial class QrScannerPage : ContentPage
{
    private readonly MainViewModel _vm;
    private bool _handled;
    private bool _isNavigating;

    public QrScannerPage(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BarcodeReader.Options = new BarcodeReaderOptions
        {
            Formats = BarcodeFormat.QrCode,
            AutoRotate = true,
            Multiple = false
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _handled = false;
        _isNavigating = false;

        try
        {
            var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
            if (status != PermissionStatus.Granted)
                status = await Permissions.RequestAsync<Permissions.Camera>();

            if (status == PermissionStatus.Granted)
            {
                PermissionHintLabel.IsVisible = false;
                BarcodeReader.IsDetecting = true;
            }
            else
            {
                PermissionHintLabel.IsVisible = true;
                BarcodeReader.IsDetecting = false;
            }
        }
        catch
        {
            PermissionHintLabel.IsVisible = true;
            BarcodeReader.IsDetecting = false;
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        try
        {
            BarcodeReader.IsDetecting = false;
            BarcodeReader.IsTorchOn = false;
        }
        catch { }
    }

    private void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
    {
        if (_handled) return;
        string? raw = e.Results?.FirstOrDefault()?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(raw)) return;

        _handled = true;
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                BarcodeReader.IsDetecting = false;
                BarcodeReader.IsTorchOn = false;
                await _vm.ImportScannedQrAsync(raw);
            }
            catch (Exception ex)
            {
                await HivoxDialog.ShowAlertAsync("Ошибка QR", ex.Message, "OK");
            }
            finally
            {
                if (!_isNavigating)
                {
                    _isNavigating = true;
                    await Shell.Current.GoToAsync("..");
                }
            }
        });
    }

    private void OnTorchTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            BarcodeReader.IsTorchOn = !BarcodeReader.IsTorchOn;
            TorchIcon.Text = BarcodeReader.IsTorchOn ? "\uE3E7" : "\uE3E6";
            TorchIcon.TextColor = BarcodeReader.IsTorchOn ? Color.FromArgb("#FBBF24") : Colors.White;
        }
        catch { }
    }

    private async void OnPickFromGalleryTapped(object? sender, TappedEventArgs e)
    {
        if (_handled || _isNavigating) return;

        try
        {
            BarcodeReader.IsDetecting = false;
            var file = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Выберите изображение с QR-кодом",
                FileTypes = FilePickerFileType.Images
            });

            if (file == null)
            {
                BarcodeReader.IsDetecting = !PermissionHintLabel.IsVisible;
                return;
            }

            string? decoded = await Task.Run(async () =>
            {
                await using var stream = await file.OpenReadAsync();
                return DecodeQrFromStream(stream);
            });

            if (string.IsNullOrWhiteSpace(decoded))
            {
                await HivoxDialog.ShowAlertAsync("QR-код не найден", "На выбранном изображении не удалось распознать QR-код.", "OK");
                BarcodeReader.IsDetecting = !PermissionHintLabel.IsVisible;
                return;
            }

            _handled = true;
            await _vm.ImportScannedQrAsync(decoded);
            _isNavigating = true;
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            Logger.Error($"Gallery QR decode error: {ex.Message}");
            await HivoxDialog.ShowAlertAsync("Ошибка", $"Не удалось прочитать изображение: {ex.Message}", "OK");
            BarcodeReader.IsDetecting = !PermissionHintLabel.IsVisible;
        }
    }

    private static string? DecodeQrFromStream(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        byte[] data = ms.ToArray();

        var boundsOpts = new Android.Graphics.BitmapFactory.Options { InJustDecodeBounds = true };
        Android.Graphics.BitmapFactory.DecodeByteArray(data, 0, data.Length, boundsOpts);

        int sampleSize = 1;
        int maxDim = Math.Max(boundsOpts.OutWidth, boundsOpts.OutHeight);
        while (maxDim / sampleSize > 1600)
            sampleSize *= 2;

        var decodeOpts = new Android.Graphics.BitmapFactory.Options
        {
            InSampleSize = sampleSize,
            InPreferredConfig = Android.Graphics.Bitmap.Config.Argb8888
        };

        using var bitmap = Android.Graphics.BitmapFactory.DecodeByteArray(data, 0, data.Length, decodeOpts);
        if (bitmap == null || bitmap.Width <= 0 || bitmap.Height <= 0)
            return null;

        int w = bitmap.Width;
        int h = bitmap.Height;
        int[] pixels = new int[w * h];
        bitmap.GetPixels(pixels, 0, w, 0, 0, w, h);

        byte[] rgb = new byte[w * h * 3];
        for (int i = 0; i < pixels.Length; i++)
        {
            int px = pixels[i];
            int idx = i * 3;
            rgb[idx] = (byte)((px >> 16) & 0xFF);
            rgb[idx + 1] = (byte)((px >> 8) & 0xFF);
            rgb[idx + 2] = (byte)(px & 0xFF);
        }

        var source = new ZXing.RGBLuminanceSource(rgb, w, h, ZXing.RGBLuminanceSource.BitmapFormat.RGB24);
        var reader = new ZXing.MultiFormatReader();
        var hints = new Dictionary<ZXing.DecodeHintType, object>
        {
            [ZXing.DecodeHintType.TRY_HARDER] = true,
            [ZXing.DecodeHintType.POSSIBLE_FORMATS] = new List<ZXing.BarcodeFormat> { ZXing.BarcodeFormat.QR_CODE }
        };

        try
        {
            var res = reader.decode(new ZXing.BinaryBitmap(new ZXing.Common.HybridBinarizer(source)), hints);
            if (!string.IsNullOrWhiteSpace(res?.Text))
                return res.Text.Trim();
        }
        catch { }

        try
        {
            var res = reader.decode(new ZXing.BinaryBitmap(new ZXing.Common.GlobalHistogramBinarizer(source)), hints);
            if (!string.IsNullOrWhiteSpace(res?.Text))
                return res.Text.Trim();
        }
        catch { }

        return null;
    }

    private async void OnCloseTapped(object? sender, TappedEventArgs e)
    {
        if (_isNavigating) return;
        _isNavigating = true;
        await Shell.Current.GoToAsync("..");
    }
}
