using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Hivox.Mobile.Services;

/// <summary>
/// Thread-safe encrypted vault for subscriptions, server URIs, proxy credentials, and HWID.
/// Secrets and master key are stored via Android KeyStore / EncryptedSharedPreferences (SecureStorage).
/// </summary>
public static class SecureVaultService
{
    public const string KeyDeviceHwid = "hivox_device_hwid";
    public const string KeySubscriptionsJson = "hivox_subscriptions_json";
    public const string KeyProxyAuthUser = "hivox_proxy_auth_user";
    public const string KeyProxyAuthPassword = "hivox_proxy_auth_password";
    private const string KeyVaultMaster = "hivox_vault_key";
    private const string VaultFileName = "vpn_vault_v2.enc";
    private static readonly object _vaultLock = new();
    private static readonly ConcurrentDictionary<string, string?> _secretCache = new(StringComparer.Ordinal);
    private static byte[]? _cachedMasterKey;
    private static string? _cachedHwid;

    public static string? GetSecret(string key)
    {
        if (_secretCache.TryGetValue(key, out string? cached))
            return cached;

        try
        {
            string? val = Task.Run(async () => await SecureStorage.Default.GetAsync(key).ConfigureAwait(false))
                .GetAwaiter().GetResult();
            _secretCache[key] = val;
            return val;
        }
        catch (Exception ex)
        {
            Logger.Log($"SecureStorage read ({key}) warning: {ex.Message}");
            return null;
        }
    }

    public static bool SetSecret(string key, string value)
    {
        if (_secretCache.TryGetValue(key, out string? existing) && string.Equals(existing, value, StringComparison.Ordinal))
            return true;

        try
        {
            Task.Run(async () =>
            {
                if (string.IsNullOrEmpty(value))
                    SecureStorage.Default.Remove(key);
                else
                    await SecureStorage.Default.SetAsync(key, value).ConfigureAwait(false);
            }).GetAwaiter().GetResult();
            _secretCache[key] = string.IsNullOrEmpty(value) ? null : value;
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"SecureStorage write ({key}) warning: {ex.Message}");
            return false;
        }
    }

    public static void RemoveSecret(string key)
    {
        _secretCache.TryRemove(key, out _);
        try { SecureStorage.Default.Remove(key); }
        catch { }
    }

    public static string GetOrCreateHwid()
    {
        if (!string.IsNullOrWhiteSpace(_cachedHwid))
            return _cachedHwid;

        string? existing = GetSecret(KeyDeviceHwid);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            _cachedHwid = existing;
            return existing;
        }

        string hwid;
        try
        {
            string? androidId = global::Android.Provider.Settings.Secure.GetString(
                global::Android.App.Application.Context.ContentResolver,
                global::Android.Provider.Settings.Secure.AndroidId);
            string seed = !string.IsNullOrEmpty(androidId)
                ? androidId
                : $"{(global::Android.OS.Build.Manufacturer)}:{(global::Android.OS.Build.Model)}:{Guid.NewGuid():N}";
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
            hwid = Convert.ToHexString(hash)[..32].ToLowerInvariant();
        }
        catch
        {
            hwid = Guid.NewGuid().ToString("N")[..32].ToLowerInvariant();
        }

        SetSecret(KeyDeviceHwid, hwid);
        _cachedHwid = hwid;
        return hwid;
    }

    private static byte[] GetOrCreateMasterKey()
    {
        if (_cachedMasterKey is { Length: 32 })
            return _cachedMasterKey;

        string? b64 = GetSecret(KeyVaultMaster);
        if (!string.IsNullOrWhiteSpace(b64))
        {
            try
            {
                byte[] existing = Convert.FromBase64String(b64);
                if (existing.Length == 32)
                {
                    _cachedMasterKey = existing;
                    return existing;
                }
            }
            catch { }
        }

        byte[] key = RandomNumberGenerator.GetBytes(32);
        SetSecret(KeyVaultMaster, Convert.ToBase64String(key));
        _cachedMasterKey = key;
        return key;
    }

    public static bool SaveEncryptedVault(string appDir, string plaintextJson)
    {
        lock (_vaultLock)
        {
            try
            {
                byte[] key = GetOrCreateMasterKey();
                byte[] plainBytes = Encoding.UTF8.GetBytes(plaintextJson);
                byte[] nonce = RandomNumberGenerator.GetBytes(12);
                byte[] tag = new byte[16];
                byte[] cipher = new byte[plainBytes.Length];

                using (var aes = new AesGcm(key, 16))
                {
                    aes.Encrypt(nonce, plainBytes, cipher, tag);
                }

                byte[] blob = new byte[12 + 16 + cipher.Length];
                Buffer.BlockCopy(nonce, 0, blob, 0, 12);
                Buffer.BlockCopy(tag, 0, blob, 12, 16);
                Buffer.BlockCopy(cipher, 0, blob, 28, cipher.Length);

                string vaultPath = Path.Combine(appDir, VaultFileName);
                string tmpPath = vaultPath + ".tmp";
                File.WriteAllBytes(tmpPath, blob);
                if (File.Exists(vaultPath))
                    File.Replace(tmpPath, vaultPath, null);
                else
                    File.Move(tmpPath, vaultPath);

                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"SaveEncryptedVault error: {ex.Message}");
                return false;
            }
        }
    }

    public static string? LoadEncryptedVault(string appDir)
    {
        lock (_vaultLock)
        {
            try
            {
                string vaultPath = Path.Combine(appDir, VaultFileName);
                if (!File.Exists(vaultPath))
                    return null;

                byte[] blob = File.ReadAllBytes(vaultPath);
                if (blob.Length < 28)
                    return null;

                string? b64 = GetSecret(KeyVaultMaster);
                if (string.IsNullOrWhiteSpace(b64))
                    return null;

                byte[] key = Convert.FromBase64String(b64);
                if (key.Length != 32)
                    return null;

                byte[] nonce = new byte[12];
                byte[] tag = new byte[16];
                byte[] cipher = new byte[blob.Length - 28];
                Buffer.BlockCopy(blob, 0, nonce, 0, 12);
                Buffer.BlockCopy(blob, 12, tag, 0, 16);
                Buffer.BlockCopy(blob, 28, cipher, 0, cipher.Length);

                byte[] plain = new byte[cipher.Length];
                using (var aes = new AesGcm(key, 16))
                {
                    aes.Decrypt(nonce, cipher, tag, plain);
                }

                return Encoding.UTF8.GetString(plain);
            }
            catch (Exception ex)
            {
                Logger.Error($"LoadEncryptedVault error: {ex.Message}");
                return null;
            }
        }
    }
}
