namespace Hivox.Mobile.Services;

public static class Logger
{
    private static string _logFile = "";
    private static readonly object _lock = new();
    private static readonly Queue<string> _recentLines = new();
    private const int MaxMemoryLines = 300;
    private const long MaxLogFileBytes = 512 * 1024;

    public static void Initialize(string appDir)
    {
        _logFile = Path.Combine(appDir, "vpn_mobile_v2.log");
        try
        {
            var dir = Path.GetDirectoryName(_logFile);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (File.Exists(_logFile) && new FileInfo(_logFile).Length > MaxLogFileBytes)
            {
                var lines = File.ReadAllLines(_logFile);
                var tail = lines.Skip(Math.Max(0, lines.Length - 200)).ToArray();
                File.WriteAllLines(_logFile, tail);
            }
        }
        catch { }
    }

    public static void Log(string message) => Write("INFO", message, false);
    public static void Error(string message) => Write("ERROR", message, true);
    public static void Debug(string message) => Write("DEBUG", message, false, logcat: false);

    private static void Write(string level, string message, bool isError, bool logcat = true)
    {
        try
        {
            string line = $"{DateTime.Now:HH:mm:ss} [{level}] {message}";
            lock (_lock)
            {
                _recentLines.Enqueue(line);
                while (_recentLines.Count > MaxMemoryLines)
                    _recentLines.Dequeue();

                if (!string.IsNullOrEmpty(_logFile))
                    File.AppendAllText(_logFile, $"{DateTime.Now:yyyy-MM-dd} {line}{Environment.NewLine}");

                System.Diagnostics.Debug.WriteLine(line);
                if (logcat)
                {
                    if (isError)
                        Android.Util.Log.Error("SaninaVPN", message);
                    else
                        Android.Util.Log.Info("SaninaVPN", message);
                }
            }
        }
        catch { }
    }

    public static string GetRecentLogs()
    {
        lock (_lock)
        {
            if (_recentLines.Count > 0)
                return string.Join(Environment.NewLine, _recentLines);
            try
            {
                if (!string.IsNullOrEmpty(_logFile) && File.Exists(_logFile))
                {
                    var lines = File.ReadAllLines(_logFile);
                    return string.Join(Environment.NewLine, lines.TakeLast(200));
                }
            }
            catch { }
            return "Логи пока пусты.";
        }
    }

    public static void ClearLogs()
    {
        lock (_lock)
        {
            _recentLines.Clear();
            try
            {
                if (!string.IsNullOrEmpty(_logFile) && File.Exists(_logFile))
                    File.WriteAllText(_logFile, "");
            }
            catch { }
        }
    }
}
