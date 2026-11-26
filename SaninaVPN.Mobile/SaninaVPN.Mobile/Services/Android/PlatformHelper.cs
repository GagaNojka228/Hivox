namespace Hivox.Mobile.Services.PlatformAndroid;

public static class PlatformHelper
{
    private static readonly SemaphoreSlim _geoLock = new(1, 1);

    public static string AppDataDir
    {
        get
        {
            try
            {
                return Android.App.Application.Context.FilesDir?.AbsolutePath
                    ?? Environment.GetFolderPath(Environment.SpecialFolder.Personal);
            }
            catch
            {
                return Environment.GetFolderPath(Environment.SpecialFolder.Personal);
            }
        }
    }

    public static async Task EnsureGeoData()
    {
        await _geoLock.WaitAsync();
        try
        {
            string geoipPath = Path.Combine(AppDataDir, "geoip.dat");
            string geositePath = Path.Combine(AppDataDir, "geosite.dat");

            if (!File.Exists(geoipPath) || new FileInfo(geoipPath).Length == 0)
            {
                Logger.Log("Extracting geoip.dat...");
                await ExtractAssetAsync("geoip.dat", geoipPath);
            }
            if (!File.Exists(geositePath) || new FileInfo(geositePath).Length == 0)
            {
                Logger.Log("Extracting geosite.dat...");
                await ExtractAssetAsync("geosite.dat", geositePath);
            }
        }
        finally
        {
            _geoLock.Release();
        }
    }

    public static async Task ExtractAssetAsync(string assetName, string destPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var assets = Android.App.Application.Context.Assets;
            if (assets == null) return;

            using var asset = assets.Open(assetName);
            using var dest = File.Create(destPath);
            await asset.CopyToAsync(dest);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to extract asset {assetName}: {ex.Message}");
        }
    }

    public static string XrayPath
    {
        get
        {
            try
            {
                var nativeDir = Android.App.Application.Context.ApplicationInfo?.NativeLibraryDir;
                if (!string.IsNullOrEmpty(nativeDir))
                {
                    string path = Path.Combine(nativeDir, "libxray.so");
                    if (File.Exists(path))
                        return path;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to locate xray binary: {ex.Message}");
            }
            return Path.Combine(AppDataDir, "libxray.so");
        }
    }

    public static string ConfigPath => Path.Combine(AppDataDir, "config.json");
}
