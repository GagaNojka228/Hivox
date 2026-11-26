using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;
using Hivox.Services;

namespace Hivox.Helpers
{
    public static class UninstallHelper
    {
        private const int MOVEFILE_DELAY_UNTIL_REBOOT = 0x00000004;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool MoveFileExW(string lpExistingFileName, string lpNewFileName, int dwFlags);

        public static void RunUninstall()
        {
            var confirm = MessageBox.Show(
                "Удалить Hivox с этого компьютера?",
                "Удаление Hivox",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            var removeDataResult = MessageBox.Show(
                "Удалить также настройки, подписки и сохранённые серверы?",
                "Пользовательские данные",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            bool removeUserData = removeDataResult == MessageBoxResult.Yes;

            try
            {
                ProcessManager.KillProcess("xray.exe");
                ProcessManager.KillProcess("sing-box.exe");
                ProxyManager.SetSystemProxy(false, Config.ProxyPort);
            }
            catch { }

            // Shortcuts
            try
            {
                string desktopLnk = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    "Hivox.lnk");
                if (File.Exists(desktopLnk)) File.Delete(desktopLnk);

                string startDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                    "Hivox");
                if (Directory.Exists(startDir)) Directory.Delete(startDir, true);
            }
            catch { }

            // Registry entries (HKCU only)
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Hivox", false); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\hivox", false); } catch { }
            try
            {
                using var runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
                runKey?.DeleteValue("Hivox", throwOnMissingValue: false);
            }
            catch { }

            if (removeUserData)
            {
                DeleteDirectorySafe(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hivox"));
                DeleteDirectorySafe(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hivox"));
            }

            string installDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string selfExe = Environment.ProcessPath ?? "";
            if (!string.IsNullOrEmpty(installDir) && Directory.Exists(installDir))
            {
                foreach (var file in Directory.GetFiles(installDir))
                {
                    if (!string.IsNullOrEmpty(selfExe) && string.Equals(file, selfExe, StringComparison.OrdinalIgnoreCase))
                        continue;
                    try { File.Delete(file); }
                    catch { MoveFileExW(file, null, MOVEFILE_DELAY_UNTIL_REBOOT); }
                }

                if (!string.IsNullOrEmpty(selfExe) && File.Exists(selfExe))
                    MoveFileExW(selfExe, null, MOVEFILE_DELAY_UNTIL_REBOOT);
                MoveFileExW(installDir, null, MOVEFILE_DELAY_UNTIL_REBOOT);
            }

            MessageBox.Show(
                "Hivox удалён. Оставшийся исполняемый файл будет автоматически очищен при перезагрузке Windows.",
                "Удаление завершено",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private static void DeleteDirectorySafe(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            try
            {
                Directory.Delete(dir, true);
            }
            catch
            {
                try
                {
                    foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                        MoveFileExW(f, null, MOVEFILE_DELAY_UNTIL_REBOOT);
                    MoveFileExW(dir, null, MOVEFILE_DELAY_UNTIL_REBOOT);
                }
                catch { }
            }
        }
    }
}
