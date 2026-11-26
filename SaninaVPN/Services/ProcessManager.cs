using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;

namespace Hivox.Services
{
    public static class ProcessManager
    {
        public static string LogDir { get; set; } = "";

        public static string LastCoreError { get; private set; } = "";

        public static string CoresDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Hivox", "cores");

        private static readonly object _extractLock = new();
        private static readonly ConcurrentDictionary<string, string> _expectedSha256Cache = new(StringComparer.OrdinalIgnoreCase);

        private static string ComputeSha256Hex(Stream stream)
        {
            stream.Position = 0;
            byte[] hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static bool VerifyFileSha256(string path, string expectedSha256)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path) || new FileInfo(path).Length < 100_000)
                    return false;
                if (string.IsNullOrEmpty(expectedSha256))
                    return true;
                using var fs = File.OpenRead(path);
                string actual = ComputeSha256Hex(fs);
                return string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Locate or extract a bundled core binary (xray.exe / sing-box.exe / wintun.dll) into %LOCALAPPDATA%\Hivox\cores with SHA-256 verification.
        /// </summary>
        public static string ResolveOrExtractCore(string exeName, string appDir)
        {
            if (string.IsNullOrWhiteSpace(exeName)) return "";

            string baseDir = string.IsNullOrWhiteSpace(appDir) ? AppDomain.CurrentDomain.BaseDirectory : appDir;
            string coresDir = CoresDirectory;
            string primaryPath = Path.Combine(coresDir, exeName);
            string resourceName = $"Hivox.Cores.{exeName}";
            var asm = Assembly.GetExecutingAssembly();

            using (var resStream = asm.GetManifestResourceStream(resourceName))
            {
                if (resStream != null)
                {
                    string expectedSha = _expectedSha256Cache.GetOrAdd(resourceName, _ => ComputeSha256Hex(resStream));
                    if (VerifyFileSha256(primaryPath, expectedSha))
                        return primaryPath;

                    lock (_extractLock)
                    {
                        if (VerifyFileSha256(primaryPath, expectedSha))
                            return primaryPath;

                        try
                        {
                            Directory.CreateDirectory(coresDir);
                            string tmpPath = primaryPath + ".tmp";
                            using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
                            {
                                resStream.Position = 0;
                                resStream.CopyTo(fs);
                            }
                            File.Move(tmpPath, primaryPath, overwrite: true);
                            if (VerifyFileSha256(primaryPath, expectedSha))
                            {
                                Logger.Log($"Verified & extracted {exeName} (SHA256={expectedSha[..12]}...) -> {primaryPath}");
                                return primaryPath;
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"Core extract to {coresDir} failed: {ex.Message}");
                        }
                    }
                }
            }

            // Fallback for unpacked/dev runs without embedded resources
            var candidates = new[]
            {
                primaryPath,
                Path.Combine(baseDir, exeName),
                Path.Combine(baseDir, "_internal", exeName)
            };

            foreach (var c in candidates)
            {
                if (IsValidCoreFile(c))
                    return c;
            }

            try
            {
                var dir = new DirectoryInfo(baseDir);
                for (int i = 0; i < 4 && dir?.Parent != null; i++)
                {
                    dir = dir.Parent;
                    string parentCandidate = Path.Combine(dir.FullName, exeName);
                    if (IsValidCoreFile(parentCandidate))
                    {
                        string copied = TryCopyToWritableDir(parentCandidate, coresDir, baseDir, exeName);
                        return !string.IsNullOrEmpty(copied) ? copied : parentCandidate;
                    }
                }
            }
            catch { }

            return primaryPath;
        }

        private static bool IsValidCoreFile(string path)
        {
            try { return !string.IsNullOrEmpty(path) && File.Exists(path) && new FileInfo(path).Length > 100_000; }
            catch { return false; }
        }

        private static string TryCopyToWritableDir(string source, string primaryDir, string fallbackDir, string exeName)
        {
            foreach (var dir in new[] { primaryDir, fallbackDir })
            {
                try
                {
                    Directory.CreateDirectory(dir);
                    string dest = Path.Combine(dir, exeName);
                    File.Copy(source, dest, overwrite: true);
                    if (IsValidCoreFile(dest)) return dest;
                }
                catch { }
            }
            return "";
        }

        public static void KillProcess(string processName)
        {
            try
            {
                string coresDir = CoresDirectory;
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string procShort = Path.GetFileNameWithoutExtension(processName);

                foreach (var proc in Process.GetProcessesByName(procShort))
                {
                    try
                    {
                        string exePath = "";
                        try { exePath = proc.MainModule?.FileName ?? ""; } catch { }
                        if (string.IsNullOrEmpty(exePath)) continue;

                        bool isOwnedByHivox =
                            exePath.StartsWith(coresDir, StringComparison.OrdinalIgnoreCase) ||
                            (!string.IsNullOrEmpty(baseDir) && exePath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase)) ||
                            (!string.IsNullOrEmpty(LogDir) && exePath.StartsWith(LogDir, StringComparison.OrdinalIgnoreCase));

                        if (isOwnedByHivox)
                        {
                            proc.Kill();
                            proc.WaitForExit(1500);
                        }
                    }
                    catch { }
                    finally { try { proc.Dispose(); } catch { } }
                }
            }
            catch { }
        }

        public static Process StartXray(string configPath, string xrayPath)
        {
            LastCoreError = "";
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = xrayPath,
                    Arguments = $"run -c \"{configPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                string assetDir = Path.GetDirectoryName(configPath) ?? AppDomain.CurrentDomain.BaseDirectory;
                if (File.Exists(Path.Combine(assetDir, "geoip.dat")) || File.Exists(Path.Combine(assetDir, "geosite.dat")))
                {
                    psi.EnvironmentVariables["XRAY_LOCATION_ASSET"] = assetDir;
                }
                var proc = Process.Start(psi);
                if (proc != null)
                {
                    proc.OutputDataReceived += (sender, args) =>
                    {
                        if (!string.IsNullOrEmpty(args.Data) && (args.Data.Contains("Failed", StringComparison.OrdinalIgnoreCase) || args.Data.Contains("error", StringComparison.OrdinalIgnoreCase)))
                        {
                            LastCoreError = args.Data;
                            Logger.Log($"[Xray OUT] {args.Data}");
                        }
                    };
                    proc.ErrorDataReceived += (sender, args) =>
                    {
                        if (!string.IsNullOrEmpty(args.Data))
                        {
                            LastCoreError = args.Data;
                            Logger.Log($"[Xray ERR] {args.Data}");
                        }
                    };
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();
                }
                return proc;
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to start Xray: {ex.Message}");
                throw;
            }
        }

        public static Process StartSingbox(string configPath, string singboxPath)
        {
            LastCoreError = "";
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = singboxPath,
                    Arguments = $"run -c \"{configPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                psi.EnvironmentVariables["ENABLE_DEPRECATED_LEGACY_DNS_SERVERS"] = "true";
                psi.EnvironmentVariables["ENABLE_DEPRECATED_OUTBOUND_DNS_RULE_ITEM"] = "true";
                psi.EnvironmentVariables["ENABLE_DEPRECATED_MISSING_DOMAIN_RESOLVER"] = "true";
                var proc = Process.Start(psi);
                if (proc != null)
                {
                    proc.OutputDataReceived += (sender, args) =>
                    {
                        if (!string.IsNullOrEmpty(args.Data) &&
                            (args.Data.Contains("FATAL", StringComparison.OrdinalIgnoreCase) ||
                             args.Data.Contains("ERROR", StringComparison.OrdinalIgnoreCase)))
                        {
                            LastCoreError = args.Data;
                            Logger.Log($"[Sing-box OUT] {args.Data}");
                        }
                    };
                    proc.ErrorDataReceived += (sender, args) =>
                    {
                        if (!string.IsNullOrEmpty(args.Data))
                        {
                            LastCoreError = args.Data;
                            Logger.Log($"[Sing-box ERR] {args.Data}");
                        }
                    };
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();
                }
                return proc;
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to start Sing-box: {ex.Message}");
                throw;
            }
        }

        public static void CleanupStderrFiles()
        {
            if (!string.IsNullOrEmpty(LogDir) && Directory.Exists(LogDir))
            {
                foreach (var f in Directory.GetFiles(LogDir, "*_stderr.log*"))
                {
                    try { File.Delete(f); } catch { }
                }
            }
        }
    }
}
