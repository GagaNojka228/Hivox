using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Hivox.Mobile.Services.PlatformAndroid;

public static class XrayProcessManager
{
    private const int FGetFd = 1;
    private const int FSetFd = 2;
    private const int FdCloexec = 1;
    private const int FDupFdCloexec = 1030;
    private const int SigTerm = 15;
    private const int SigKill = 9;
    private const int WNoHang = 1;
    private const int EIntr = 4;
    private const int ChildTunFd = 3;

    [DllImport("c", SetLastError = true)]
    private static extern int pipe(int[] fds);

    [DllImport("c", SetLastError = true)]
    private static extern int fcntl(int fd, int cmd, int arg);

    [DllImport("c", SetLastError = true)]
    private static extern int close(int fd);

    [DllImport("c", SetLastError = true)]
    private static extern nint read(int fd, byte[] buf, nuint count);

    [DllImport("c", SetLastError = true)]
    private static extern int kill(int pid, int sig);

    [DllImport("c", SetLastError = true)]
    private static extern int waitpid(int pid, out int status, int options);

    [DllImport("c", SetLastError = true)]
    private static extern int posix_spawn_file_actions_init(out IntPtr actions);

    [DllImport("c", SetLastError = true)]
    private static extern int posix_spawn_file_actions_destroy(ref IntPtr actions);

    [DllImport("c", SetLastError = true)]
    private static extern int posix_spawn_file_actions_adddup2(ref IntPtr actions, int fd, int newFd);

    [DllImport("c", SetLastError = true)]
    private static extern int posix_spawn_file_actions_addclose(ref IntPtr actions, int fd);

    [DllImport("c", SetLastError = true)]
    private static extern int posix_spawn(
        out int pid,
        string path,
        ref IntPtr fileActions,
        IntPtr attrp,
        IntPtr[] argv,
        IntPtr[] envp);

    private static Process? _currentProcess;
    private static int _nativePid = -1;
    private static readonly object _lock = new();
    private static string _lastError = "";

    public static string LastError => _lastError;

    public static bool IsRunning
    {
        get
        {
            lock (_lock)
            {
                if (_nativePid > 0)
                {
                    try
                    {
                        int w = waitpid(_nativePid, out _, WNoHang);
                        if (w == 0)
                            return true;
                        _nativePid = -1;
                        return false;
                    }
                    catch
                    {
                        _nativePid = -1;
                        return false;
                    }
                }

                try
                {
                    return _currentProcess != null && !_currentProcess.HasExited;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }
        }
    }

    public static void KillAll()
    {
        lock (_lock)
        {
            if (_nativePid > 0)
            {
                int pid = _nativePid;
                _nativePid = -1;
                try
                {
                    kill(pid, SigTerm);
                    bool reaped = false;
                    for (int i = 0; i < 10; i++)
                    {
                        int w = waitpid(pid, out _, WNoHang);
                        if (w == pid || w < 0)
                        {
                            reaped = true;
                            break;
                        }
                        Thread.Sleep(50);
                    }
                    if (!reaped)
                    {
                        kill(pid, SigKill);
                        waitpid(pid, out _, 0);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error killing native xray pid={pid}: {ex.Message}");
                }
            }

            try
            {
                if (_currentProcess != null)
                {
                    try
                    {
                        if (!_currentProcess.HasExited)
                        {
                            _currentProcess.Kill(entireProcessTree: true);
                            _currentProcess.WaitForExit(2000);
                        }
                    }
                    catch (InvalidOperationException) { }
                    catch (Exception) { }
                    _currentProcess.Dispose();
                    _currentProcess = null;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error killing xray: {ex.Message}");
            }
        }
    }

    public static async Task<bool> StartAsync(string configPath, string binaryPath, int? tunFd = null)
    {
        string workDir = PlatformHelper.AppDataDir;

        if (tunFd.HasValue && OperatingSystem.IsAndroidVersionAtLeast(28))
            return await StartWithPosixSpawnAsync(configPath, binaryPath, tunFd.Value, workDir);

        Process? proc = null;
        var stderrBuilder = new StringBuilder();

        lock (_lock)
        {
            try
            {
                KillAll();
                _lastError = "";

                if (!File.Exists(binaryPath))
                {
                    _lastError = $"Ядро не найдено: {binaryPath}";
                    Logger.Error(_lastError);
                    return false;
                }

                if (!File.Exists(configPath))
                {
                    _lastError = $"Конфиг не найден: {configPath}";
                    Logger.Error(_lastError);
                    return false;
                }

                Logger.Log($"Starting xray via Process.Start: {binaryPath} (tunFd={tunFd?.ToString() ?? "none"})");

                var psi = new ProcessStartInfo
                {
                    FileName = binaryPath,
                    Arguments = $"run -c \"{configPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = workDir
                };

                psi.EnvironmentVariables["XRAY_LOCATION_ASSET"] = workDir;
                psi.EnvironmentVariables["HOME"] = workDir;
                psi.EnvironmentVariables["TMPDIR"] = workDir;
                if (!Config.MemoryUnlimited && Config.MemoryLimitMb > 0)
                    psi.EnvironmentVariables["GOMEMLIMIT"] = $"{Config.MemoryLimitMb}MiB";
                if (tunFd.HasValue)
                {
                    psi.EnvironmentVariables["XRAY_TUN_FD"] = tunFd.Value.ToString();
                    psi.EnvironmentVariables["xray.tun.fd"] = tunFd.Value.ToString();
                }

                _currentProcess = Process.Start(psi);
                proc = _currentProcess;

                if (proc == null)
                {
                    _lastError = "Не удалось запустить процесс Xray";
                    Logger.Error(_lastError);
                    return false;
                }

                proc.OutputDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        Logger.Log($"[xray] {e.Data}");
                };
                proc.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Logger.Error($"[xray] {e.Data}");
                        lock (stderrBuilder)
                            stderrBuilder.AppendLine(e.Data);
                    }
                };

                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                _lastError = $"Ошибка запуска Xray: {ex.Message}";
                Logger.Error(_lastError);
                return false;
            }
        }

        // Wait asynchronously without blocking UI thread
        await Task.Delay(750);

        lock (_lock)
        {
            try
            {
                if (_currentProcess == null || _currentProcess != proc)
                {
                    _lastError = "Процесс Xray был остановлен";
                    return false;
                }

                if (_currentProcess.HasExited)
                {
                    int exitCode = _currentProcess.ExitCode;
                    string err;
                    lock (stderrBuilder)
                        err = stderrBuilder.ToString().Trim();
                    _lastError = string.IsNullOrEmpty(err)
                        ? $"Xray завершился с кодом {exitCode}"
                        : $"Xray ({exitCode}): {err}";
                    Logger.Error(_lastError);
                    _currentProcess.Dispose();
                    _currentProcess = null;
                    return false;
                }

                Logger.Log($"Xray running, PID={_currentProcess.Id}");
                return true;
            }
            catch (Exception ex)
            {
                _lastError = $"Ошибка проверки Xray: {ex.Message}";
                Logger.Error(_lastError);
                _currentProcess = null;
                return false;
            }
        }
    }

    private static async Task<bool> StartWithPosixSpawnAsync(
        string configPath, string binaryPath, int tunFd, string workDir)
    {
        var outputBuilder = new StringBuilder();
        Task? pipeTask = null;
        int pid = -1;

        lock (_lock)
        {
            KillAll();
            _lastError = "";

            if (!File.Exists(binaryPath))
            {
                _lastError = $"Ядро не найдено: {binaryPath}";
                Logger.Error(_lastError);
                return false;
            }

            if (!File.Exists(configPath))
            {
                _lastError = $"Конфиг не найден: {configPath}";
                Logger.Error(_lastError);
                return false;
            }

            if (tunFd < 0)
            {
                _lastError = $"Неверный TUN FD: {tunFd}";
                Logger.Error(_lastError);
                return false;
            }

            Logger.Log($"Starting xray via posix_spawn: {binaryPath} (parentTunFd={tunFd} -> childFd={ChildTunFd})");

            int[] pipeFds = new int[2];
            if (pipe(pipeFds) != 0)
            {
                int errno = Marshal.GetLastPInvokeError();
                _lastError = $"Не удалось создать pipe (errno {errno})";
                Logger.Error(_lastError);
                return false;
            }

            int pipeRead = pipeFds[0];
            int pipeWrite = pipeFds[1];

            // Keep pipe fds clear of 0..3 so adddup2(..., ChildTunFd=3) never collides with the pipe
            if (pipeRead <= ChildTunFd)
            {
                int moved = fcntl(pipeRead, FDupFdCloexec, 10);
                close(pipeRead);
                pipeRead = moved;
            }
            else
            {
                fcntl(pipeRead, FSetFd, FdCloexec);
            }

            if (pipeWrite <= ChildTunFd)
            {
                int moved = fcntl(pipeWrite, FDupFdCloexec, 10);
                close(pipeWrite);
                pipeWrite = moved;
            }
            else
            {
                fcntl(pipeWrite, FSetFd, FdCloexec);
            }

            if (pipeRead < 0 || pipeWrite < 0)
            {
                if (pipeRead >= 0) close(pipeRead);
                if (pipeWrite >= 0) close(pipeWrite);
                _lastError = "Не удалось подготовить дескрипторы pipe для Xray";
                Logger.Error(_lastError);
                return false;
            }

            // Ensure parent's tunFd is inheritable when tunFd == ChildTunFd
            int tunFlags = fcntl(tunFd, FGetFd, 0);
            if (tunFlags >= 0 && (tunFlags & FdCloexec) != 0)
                fcntl(tunFd, FSetFd, tunFlags & ~FdCloexec);

            IntPtr actions = IntPtr.Zero;
            bool actionsInitialized = false;
            var allocs = new List<IntPtr>();

            try
            {
                int initRc = posix_spawn_file_actions_init(out actions);
                if (initRc != 0)
                {
                    close(pipeRead);
                    close(pipeWrite);
                    _lastError = $"posix_spawn_file_actions_init ошибка ({initRc})";
                    Logger.Error(_lastError);
                    return false;
                }
                actionsInitialized = true;

                posix_spawn_file_actions_adddup2(ref actions, pipeWrite, 1);
                posix_spawn_file_actions_adddup2(ref actions, pipeWrite, 2);
                posix_spawn_file_actions_addclose(ref actions, pipeRead);
                posix_spawn_file_actions_addclose(ref actions, pipeWrite);

                if (tunFd != ChildTunFd)
                {
                    posix_spawn_file_actions_adddup2(ref actions, tunFd, ChildTunFd);
                    posix_spawn_file_actions_addclose(ref actions, tunFd);
                }

                IntPtr AllocUtf8(string s)
                {
                    var p = Marshal.StringToCoTaskMemUTF8(s);
                    allocs.Add(p);
                    return p;
                }

                var argvPtrs = new IntPtr[]
                {
                    AllocUtf8(binaryPath),
                    AllocUtf8("run"),
                    AllocUtf8("-c"),
                    AllocUtf8(configPath),
                    IntPtr.Zero
                };

                var envList = new List<IntPtr>
                {
                    AllocUtf8($"XRAY_LOCATION_ASSET={workDir}"),
                    AllocUtf8($"XRAY_TUN_FD={ChildTunFd}"),
                    AllocUtf8($"xray.tun.fd={ChildTunFd}"),
                    AllocUtf8($"HOME={workDir}"),
                    AllocUtf8($"TMPDIR={workDir}"),
                    AllocUtf8("PATH=/system/bin:/vendor/bin")
                };
                if (!Config.MemoryUnlimited && Config.MemoryLimitMb > 0)
                    envList.Add(AllocUtf8($"GOMEMLIMIT={Config.MemoryLimitMb}MiB"));
                envList.Add(IntPtr.Zero);
                var envpPtrs = envList.ToArray();

                int spawnRc = posix_spawn(out pid, binaryPath, ref actions, IntPtr.Zero, argvPtrs, envpPtrs);
                close(pipeWrite);
                pipeWrite = -1;

                if (spawnRc != 0)
                {
                    close(pipeRead);
                    _lastError = $"posix_spawn ошибка (код {spawnRc})";
                    Logger.Error(_lastError);
                    return false;
                }

                _nativePid = pid;
                int readerFd = pipeRead;
                pipeRead = -1;

                pipeTask = Task.Run(() => ReadPipeLoop(readerFd, outputBuilder));
            }
            catch (Exception ex)
            {
                if (pipeWrite >= 0) close(pipeWrite);
                if (pipeRead >= 0) close(pipeRead);
                _lastError = $"Ошибка posix_spawn Xray: {ex.Message}";
                Logger.Error(_lastError);
                return false;
            }
            finally
            {
                foreach (var ptr in allocs)
                    Marshal.FreeCoTaskMem(ptr);
                if (actionsInitialized)
                    posix_spawn_file_actions_destroy(ref actions);
            }
        }

        // Wait asynchronously without blocking UI thread
        await Task.Delay(750);

        bool exitedEarly = false;
        int earlyStatus = 0;
        lock (_lock)
        {
            if (_nativePid != pid || pid <= 0)
            {
                _lastError = "Процесс Xray был остановлен";
                return false;
            }

            int waited = waitpid(pid, out earlyStatus, WNoHang);
            if (waited == 0)
            {
                Logger.Log($"Xray running via posix_spawn, PID={pid}, childTunFd={ChildTunFd}");
                return true;
            }

            _nativePid = -1;
            exitedEarly = true;
        }

        if (exitedEarly && pipeTask != null)
        {
            try { await Task.WhenAny(pipeTask, Task.Delay(150)); } catch { }
        }

        lock (_lock)
        {
            string reason;
            if ((earlyStatus & 0x7F) == 0)
            {
                int exitCode = (earlyStatus >> 8) & 0xFF;
                reason = $"код {exitCode}";
            }
            else
            {
                int sig = earlyStatus & 0x7F;
                reason = $"сигнал {sig}";
            }

            string tail;
            lock (outputBuilder)
                tail = outputBuilder.ToString().Trim();
            if (tail.Length > 600)
                tail = tail[^600..];

            _lastError = string.IsNullOrEmpty(tail)
                ? $"Xray завершился ({reason})"
                : $"Xray ({reason}): {tail}";
            Logger.Error(_lastError);
            return false;
        }
    }

    private static void ReadPipeLoop(int readFd, StringBuilder outputBuilder)
    {
        var buf = new byte[4096];
        var decoder = Encoding.UTF8.GetDecoder();
        var charBuf = new char[4096];
        var lineSb = new StringBuilder();

        try
        {
            while (true)
            {
                nint n = read(readFd, buf, (nuint)buf.Length);
                if (n < 0)
                {
                    if (Marshal.GetLastPInvokeError() == EIntr)
                        continue;
                    break;
                }
                if (n == 0)
                    break;

                int charCount = decoder.GetChars(buf, 0, (int)n, charBuf, 0, flush: false);
                for (int i = 0; i < charCount; i++)
                {
                    char ch = charBuf[i];
                    if (ch == '\r') continue;
                    if (ch == '\n')
                    {
                        FlushLogLine(lineSb, outputBuilder);
                    }
                    else
                    {
                        lineSb.Append(ch);
                    }
                }
            }

            if (lineSb.Length > 0)
                FlushLogLine(lineSb, outputBuilder);
        }
        catch { }
        finally
        {
            try { close(readFd); } catch { }
        }
    }

    private static void FlushLogLine(StringBuilder lineSb, StringBuilder outputBuilder)
    {
        string line = lineSb.ToString().Trim();
        lineSb.Clear();
        if (string.IsNullOrEmpty(line)) return;

        Logger.Log($"[xray] {line}");
        lock (outputBuilder)
        {
            if (outputBuilder.Length > 4096)
                outputBuilder.Remove(0, outputBuilder.Length - 2048);
            outputBuilder.AppendLine(line);
        }
    }

    public static void Stop()
    {
        KillAll();
        Logger.Log("Xray process stopped");
    }
}
