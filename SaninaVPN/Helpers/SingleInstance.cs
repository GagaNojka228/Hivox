using System;
using System.Threading;

namespace Hivox.Helpers
{
    public static class SingleInstance
    {
        private static Mutex _mutex;

        public static bool EnsureSingleInstance(string appName)
        {
            _mutex = new Mutex(true, appName, out bool createdNew);
            if (!createdNew)
            {
                return false;
            }
            return true;
        }

        public static void Release()
        {
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            _mutex = null;
        }
    }
}
