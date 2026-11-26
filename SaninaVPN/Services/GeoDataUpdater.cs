using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Hivox.Services
{
    public static class GeoDataUpdater
    {
        public const string GeoIpUrl = "https://github.com/Loyalsoldier/v2ray-rules-dat/releases/latest/download/geoip.dat";
        public const string GeoIpShaUrl = "https://github.com/Loyalsoldier/v2ray-rules-dat/releases/latest/download/geoip.dat.sha256sum";
        public const string GeoSiteUrl = "https://github.com/Loyalsoldier/v2ray-rules-dat/releases/latest/download/geosite.dat";
        public const string GeoSiteShaUrl = "https://github.com/Loyalsoldier/v2ray-rules-dat/releases/latest/download/geosite.dat.sha256sum";

        public sealed record GeoFileStatus(string FileName, long SizeBytes, string Sha256Prefix, bool Exists);

        public static (GeoFileStatus GeoIp, GeoFileStatus GeoSite) GetLocalStatus(string appDir)
        {
            static GeoFileStatus Inspect(string dir, string fileName)
            {
                string path = Path.Combine(dir, fileName);
                if (!File.Exists(path))
                    return new GeoFileStatus(fileName, 0, "—", false);

                try
                {
                    var info = new FileInfo(path);
                    using var fs = File.OpenRead(path);
                    byte[] hash = SHA256.HashData(fs);
                    string hex = Convert.ToHexString(hash).ToLowerInvariant()[..16] + "...";
                    return new GeoFileStatus(fileName, info.Length, hex, info.Length > 0);
                }
                catch
                {
                    return new GeoFileStatus(fileName, 0, "—", false);
                }
            }

            return (Inspect(appDir, "geoip.dat"), Inspect(appDir, "geosite.dat"));
        }

        public static async Task<(string GeoIpSha, string GeoSiteSha)> UpdateAllAsync(
            string appDir,
            bool useProxy,
            IProgress<string> progress = null,
            CancellationToken ct = default)
        {
            try
            {
                return await UpdatePairAsync(appDir, useProxy, progress, ct).ConfigureAwait(false);
            }
            catch when (useProxy && !ct.IsCancellationRequested)
            {
                progress?.Report("Повторная загрузка напрямую...");
                return await UpdatePairAsync(appDir, useProxy: false, progress, ct).ConfigureAwait(false);
            }
        }

        private static async Task<(string GeoIpSha, string GeoSiteSha)> UpdatePairAsync(
            string appDir,
            bool useProxy,
            IProgress<string> progress,
            CancellationToken ct)
        {
            Directory.CreateDirectory(appDir);
            string ipDest = Path.Combine(appDir, "geoip.dat");
            string siteDest = Path.Combine(appDir, "geosite.dat");

            var (_, ipSha) = await UpdateGeoFileAsync("geoip.dat", GeoIpUrl, GeoIpShaUrl, ipDest, useProxy, progress, ct).ConfigureAwait(false);
            var (_, siteSha) = await UpdateGeoFileAsync("geosite.dat", GeoSiteUrl, GeoSiteShaUrl, siteDest, useProxy, progress, ct).ConfigureAwait(false);
            return (ipSha, siteSha);
        }

        public static async Task<(long SizeBytes, string Sha256Short)> UpdateGeoFileAsync(
            string name,
            string fileUrl,
            string shaUrl,
            string destPath,
            bool useProxy,
            IProgress<string> progress,
            CancellationToken ct)
        {
            using var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                UseProxy = useProxy,
                Proxy = useProxy ? Config.CreateLocalHttpProxy() : null
            };
            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMinutes(3)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Hivox/2.0");

            progress?.Report($"Загрузка контрольной суммы {name}...");
            string shaText = await client.GetStringAsync(shaUrl, ct).ConfigureAwait(false);
            var match = Regex.Match(shaText.Trim(), @"\b([0-9a-fA-F]{64})\b");
            if (!match.Success)
                throw new InvalidDataException($"Некорректный формат контрольной суммы для {name}.");
            string expectedSha = match.Groups[1].Value.ToLowerInvariant();

            string tmpPath = destPath + ".tmp";
            try
            {
                progress?.Report($"Скачивание {name}...");
                using var response = await client.GetAsync(fileUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                long totalBytes = 0;
                using (var netStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                using (var fileStream = File.Create(tmpPath))
                using (var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                {
                    byte[] buf = new byte[65536];
                    int read;
                    while ((read = await netStream.ReadAsync(buf, 0, buf.Length, ct).ConfigureAwait(false)) > 0)
                    {
                        await fileStream.WriteAsync(buf, 0, read, ct).ConfigureAwait(false);
                        hasher.AppendData(buf, 0, read);
                        totalBytes += read;
                    }

                    byte[] digest = hasher.GetHashAndReset();
                    string actualSha = Convert.ToHexString(digest).ToLowerInvariant();
                    if (!string.Equals(actualSha, expectedSha, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"Ошибка SHA-256 для {name}: файл повреждён или подменён.");
                }

                if (totalBytes < 10240)
                    throw new InvalidDataException($"Файл {name} подозрительно мал ({totalBytes} Б).");

                if (File.Exists(destPath))
                    File.Replace(tmpPath, destPath, null);
                else
                    File.Move(tmpPath, destPath);

                Logger.Log($"Geo database {name} updated & verified (SHA256={expectedSha[..16]}..., size={totalBytes})");
                return (totalBytes, expectedSha[..16] + "...");
            }
            catch
            {
                try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
                throw;
            }
        }
    }
}
