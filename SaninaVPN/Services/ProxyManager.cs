using System;
using Microsoft.Win32;

namespace Hivox.Services
{
    public static class ProxyManager
    {
        public static void SetSystemProxy(bool enabled, int port)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true);
                if (key != null)
                {
                    key.SetValue("ProxyEnable", enabled ? 1 : 0, RegistryValueKind.DWord);
                    if (enabled)
                    {
                        var existingPac = key.GetValue("AutoConfigURL")?.ToString();
                        if (!string.IsNullOrWhiteSpace(existingPac))
                        {
                            key.SetValue("HivoxSavedAutoConfigURL", existingPac, RegistryValueKind.String);
                            key.DeleteValue("AutoConfigURL", false);
                        }
                        key.SetValue("ProxyServer", $"127.0.0.1:{port}", RegistryValueKind.String);
                        key.SetValue("ProxyOverride", "localhost;127.*;10.*;172.16.*;192.168.*;<local>", RegistryValueKind.String);
                    }
                    else
                    {
                        var savedPac = key.GetValue("HivoxSavedAutoConfigURL")?.ToString();
                        if (!string.IsNullOrWhiteSpace(savedPac))
                        {
                            key.SetValue("AutoConfigURL", savedPac, RegistryValueKind.String);
                            key.DeleteValue("HivoxSavedAutoConfigURL", false);
                        }
                    }
                }
                // Notify system about proxy change
                NativeMethods.InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0);
                NativeMethods.InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0);
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to set proxy: {ex.Message}");
            }
        }

        private static class NativeMethods
        {
            [System.Runtime.InteropServices.DllImport("wininet.dll", SetLastError = true)]
            public static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);
        }
    }
}
