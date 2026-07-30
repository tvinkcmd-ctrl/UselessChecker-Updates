using System;
using System.IO;

namespace UselessChecker
{
    // Простейший потокобезопасный логгер в %TEMP%. Инфраструктурная утилита.
    public static class Logger
    {
        private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "UselessChecker.log");
        private static readonly object LockObj = new object();

        public static void Error(string context, Exception? ex)
        {
            try
            {
                lock (LockObj)
                {
                    File.AppendAllText(LogPath,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ERROR [{context}]: {ex?.Message}\n{ex?.StackTrace}\n\n");
                }
            }
            catch { }
        }

        public static void Info(string message)
        {
            try
            {
                lock (LockObj)
                {
                    File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] INFO: {message}\n");
                }
            }
            catch { }
        }
    }
}