using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace HivoxInstaller
{
    public partial class MainWindow : Window
    {
        private readonly bool _uninstallMode;
        private bool _completed;
        private string _installedDir = "";

        public MainWindow(bool uninstallMode)
        {
            InitializeComponent();
            _uninstallMode = uninstallMode;

            string defaultDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Hivox");
            InstallPathBox.Text = defaultDir;

            if (_uninstallMode)
            {
                WindowHeaderTitle.Text = "Удаление Hivox";
                HeroTitle.Text = "Удаление Hivox";
                HeroSubtitle.Text = "Удаление программы с этого компьютера";
                InstallPanel.Visibility = Visibility.Collapsed;
                ProgressPanel.Visibility = Visibility.Visible;
                ProgressStatusText.Text = "Удалить Hivox и ярлыки?";
                ProgressDetailText.Text = AppDomain.CurrentDomain.BaseDirectory;
                RemoveUserDataCheck.Visibility = Visibility.Visible;
                PrimaryActionBtn.Content = "Удалить";
            }
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog
            {
                Title = "Выберите папку установки Hivox",
                InitialDirectory = Directory.Exists(InstallPathBox.Text)
                    ? InstallPathBox.Text
                    : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            };
            if (dlg.ShowDialog() == true)
            {
                string chosen = dlg.FolderName;
                if (!chosen.EndsWith("Hivox", StringComparison.OrdinalIgnoreCase))
                    chosen = Path.Combine(chosen, "Hivox");
                InstallPathBox.Text = chosen;
            }
        }

        private async void PrimaryAction_Click(object sender, RoutedEventArgs e)
        {
            if (_completed)
            {
                if (!_uninstallMode && LaunchAfterCheck.IsChecked == true)
                {
                    string exePath = Path.Combine(_installedDir, "Hivox.exe");
                    if (File.Exists(exePath))
                    {
                        try { Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true, WorkingDirectory = _installedDir }); }
                        catch { }
                    }
                }
                Close();
                return;
            }

            if (_uninstallMode)
            {
                await RunUninstallAsync();
                return;
            }

            await RunInstallAsync();
        }

        private static void KillRunningProcesses(string targetDir = null)
        {
            string coresDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Hivox", "cores");
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            foreach (var name in new[] { "Hivox", "SaninaVPN", "xray", "sing-box" })
            {
                try
                {
                    foreach (var p in Process.GetProcessesByName(name))
                    {
                        try
                        {
                            if (name is "xray" or "sing-box")
                            {
                                string path = "";
                                try { path = p.MainModule?.FileName ?? ""; } catch { }
                                bool isHivoxCore = !string.IsNullOrEmpty(path) && (
                                    path.StartsWith(coresDir, StringComparison.OrdinalIgnoreCase) ||
                                    (!string.IsNullOrEmpty(targetDir) && path.StartsWith(targetDir, StringComparison.OrdinalIgnoreCase)) ||
                                    (!string.IsNullOrEmpty(baseDir) && path.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase)));
                                if (!isHivoxCore) continue;
                            }
                            p.Kill();
                            p.WaitForExit(1500);
                        }
                        catch { }
                        finally { try { p.Dispose(); } catch { } }
                    }
                }
                catch { }
            }
        }

        private async Task RunInstallAsync()
        {
            string targetDir = (InstallPathBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(targetDir))
            {
                MessageBox.Show("Укажите папку установки.", "Hivox");
                return;
            }

            PrimaryActionBtn.IsEnabled = false;
            InstallPanel.Visibility = Visibility.Collapsed;
            ProgressPanel.Visibility = Visibility.Visible;
            ProgressStatusText.Text = "Остановка запущенных процессов...";
            InstallProgress.Value = 10;

            bool makeDesktop = DesktopShortcutCheck.IsChecked == true;
            bool makeStartMenu = StartMenuShortcutCheck.IsChecked == true;

            try
            {
                await Task.Run(() =>
                {
                    KillRunningProcesses(targetDir);
                    Directory.CreateDirectory(targetDir);

                    using var payloadStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Hivox.Payload.zip");
                    if (payloadStream == null)
                        throw new Exception("Пакет установки повреждён (не найден Hivox.Payload.zip).");

                    using var archive = new ZipArchive(payloadStream, ZipArchiveMode.Read);
                    int total = Math.Max(1, archive.Entries.Count);
                    int done = 0;

                    foreach (var entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name)) continue;
                        string destPath = Path.Combine(targetDir, entry.FullName);
                        string parent = Path.GetDirectoryName(destPath);
                        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

                        Dispatcher.Invoke(() =>
                        {
                            ProgressStatusText.Text = $"Распаковка: {entry.Name}";
                            ProgressDetailText.Text = targetDir;
                            InstallProgress.Value = 15 + (70.0 * done / total);
                        });

                        entry.ExtractToFile(destPath, overwrite: true);
                        done++;
                    }

                    Dispatcher.Invoke(() =>
                    {
                        ProgressStatusText.Text = "Регистрация в Windows и создание ярлыков...";
                        InstallProgress.Value = 90;
                    });

                    string appExe = Path.Combine(targetDir, "Hivox.exe");
                    Dispatcher.Invoke(() =>
                    {
                        if (makeDesktop)
                        {
                            string desktopLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Hivox.lnk");
                            CreateShortcut(desktopLnk, appExe, targetDir, "Hivox VPN");
                        }
                        if (makeStartMenu)
                        {
                            string startDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Hivox");
                            Directory.CreateDirectory(startDir);
                            CreateShortcut(Path.Combine(startDir, "Hivox.lnk"), appExe, targetDir, "Hivox VPN");
                            CreateShortcut(Path.Combine(startDir, "Удалить Hivox.lnk"), appExe, targetDir, "Удалить Hivox", "--uninstall");
                        }
                    });

                    RegisterUninstall(targetDir, appExe);
                });

                _installedDir = targetDir;
                _completed = true;
                InstallProgress.Value = 100;
                ProgressStatusText.Text = "Hivox успешно установлен!";
                ProgressDetailText.Text = _installedDir;
                PrimaryActionBtn.Content = "Готово";
                PrimaryActionBtn.IsEnabled = true;
            }
            catch (Exception ex)
            {
                InstallProgress.Value = 0;
                ProgressStatusText.Text = "Ошибка установки";
                ProgressDetailText.Text = ex.Message;
                PrimaryActionBtn.IsEnabled = true;
                MessageBox.Show($"Не удалось завершить установку:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task RunUninstallAsync()
        {
            PrimaryActionBtn.IsEnabled = false;
            RemoveUserDataCheck.IsEnabled = false;
            bool removeUserData = RemoveUserDataCheck.IsChecked == true;
            string dir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');

            try
            {
                await Task.Run(() =>
                {
                    KillRunningProcesses(dir);
                    Dispatcher.Invoke(() => { ProgressStatusText.Text = "Удаление ярлыков и записи Windows..."; InstallProgress.Value = 40; });

                    try
                    {
                        string desktopLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Hivox.lnk");
                        if (File.Exists(desktopLnk)) File.Delete(desktopLnk);
                        string startDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Hivox");
                        if (Directory.Exists(startDir)) Directory.Delete(startDir, true);
                    }
                    catch { }

                    try
                    {
                        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Hivox", false);
                    }
                    catch { }

                    if (removeUserData)
                    {
                        try
                        {
                            string roaming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hivox");
                            if (Directory.Exists(roaming)) Directory.Delete(roaming, true);
                            string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hivox");
                            if (Directory.Exists(local)) Directory.Delete(local, true);
                        }
                        catch { }
                    }

                    Dispatcher.Invoke(() => { ProgressStatusText.Text = "Удаление файлов программы..."; InstallProgress.Value = 80; });

                    foreach (var f in Directory.GetFiles(dir))
                    {
                        try { File.Delete(f); }
                        catch { MoveFileExW(f, null, MOVEFILE_DELAY_UNTIL_REBOOT); }
                    }
                });

                string selfExe = Environment.ProcessPath ?? "";
                if (!string.IsNullOrEmpty(selfExe) && File.Exists(selfExe))
                    MoveFileExW(selfExe, null, MOVEFILE_DELAY_UNTIL_REBOOT);
                MoveFileExW(dir, null, MOVEFILE_DELAY_UNTIL_REBOOT);

                _completed = true;
                InstallProgress.Value = 100;
                ProgressStatusText.Text = "Hivox удалён.";
                ProgressDetailText.Text = "Спасибо за использование Hivox.";
                PrimaryActionBtn.Content = "Закрыть";
                PrimaryActionBtn.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка удаления: {ex.Message}", "Ошибка");
                PrimaryActionBtn.IsEnabled = true;
            }
        }

        private const int MOVEFILE_DELAY_UNTIL_REBOOT = 0x00000004;

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern bool MoveFileExW(string lpExistingFileName, string lpNewFileName, int dwFlags);

        [System.Runtime.InteropServices.ComImport]
        [System.Runtime.InteropServices.Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink { }

        [System.Runtime.InteropServices.ComImport]
        [System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
        [System.Runtime.InteropServices.Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([System.Runtime.InteropServices.Out, System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile, int cchMaxPath, IntPtr pfd, int fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([System.Runtime.InteropServices.Out, System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cchMaxName);
            void SetDescription([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([System.Runtime.InteropServices.Out, System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cchMaxPath);
            void SetWorkingDirectory([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([System.Runtime.InteropServices.Out, System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cchMaxPath);
            void SetArguments([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([System.Runtime.InteropServices.Out, System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath, int cchIconPath, out int piIcon);
            void SetIconLocation([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
            void Resolve(IntPtr hwnd, int fFlags);
            void SetPath([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string pszFile);
        }

        private static void RegisterUninstall(string installDir, string appExe)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Hivox");
                key.SetValue("DisplayName", "Hivox");
                key.SetValue("DisplayVersion", "2.1.1");
                key.SetValue("Publisher", "Hivox");
                key.SetValue("InstallLocation", installDir);
                key.SetValue("DisplayIcon", appExe);
                key.SetValue("UninstallString", $"\"{appExe}\" --uninstall");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue("EstimatedSize", 98000, RegistryValueKind.DWord);
            }
            catch { }
        }

        private static void CreateShortcut(string shortcutPath, string targetPath, string workingDir, string description, string args = "")
        {
            try
            {
                var link = (IShellLinkW)new ShellLink();
                link.SetPath(targetPath);
                link.SetWorkingDirectory(workingDir);
                link.SetDescription(description);
                link.SetIconLocation(targetPath, 0);
                if (!string.IsNullOrEmpty(args))
                    link.SetArguments(args);

                var file = (System.Runtime.InteropServices.ComTypes.IPersistFile)link;
                file.Save(shortcutPath, false);
            }
            catch { }
        }
    }
}
