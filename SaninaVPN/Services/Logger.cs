using System;
using System.IO;

namespace Hivox.Services
{
    public static class Logger
    {
        public static readonly string LogDirectory = DataManager.ResolveDataDirectory();
        public static readonly string LogFile = Path.Combine(LogDirectory, "vpn_desktop_v2.log");
        private static readonly object _lock = new();

        static Logger()
        {
            try
            {
                var dir = Path.GetDirectoryName(LogFile);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
            }
            catch { }
        }

        public static void Log(string message)
        {
            try
            {
                lock (_lock)
                {
                    string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - INFO - {message}";
                    File.AppendAllText(LogFile, line + Environment.NewLine);
                    System.Diagnostics.Debug.WriteLine(line);
                }
            }
            catch { }
        }

        public static void Error(string message)
        {
            try
            {
                lock (_lock)
                {
                    string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - ERROR - {message}";
                    File.AppendAllText(LogFile, line + Environment.NewLine);
                    System.Diagnostics.Debug.WriteLine(line);
                }
            }
            catch { }
        }

        public static void Debug(string message)
        {
            try
            {
                lock (_lock)
                {
                    string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - DEBUG - {message}";
                    File.AppendAllText(LogFile, line + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}
