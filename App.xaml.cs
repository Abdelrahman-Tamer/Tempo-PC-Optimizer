using System;
using System.Security;
using System.Windows;

namespace Tempo
{
    public partial class App : System.Windows.Application
    {
        private static readonly object _logLock = new();
        private const long MaxLogSizeBytes = 5 * 1024 * 1024; // 5 MB rotation threshold

        private static void LogError(string level, string message)
        {
            try
            {
                lock (_logLock)
                {
                    string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tempo");
                    if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                    string logFile = System.IO.Path.Combine(dir, "error.log");

                    var fi = new System.IO.FileInfo(logFile);
                    if (fi.Exists && fi.Length > MaxLogSizeBytes)
                    {
                        string oldLog = logFile + ".old";
                        System.IO.File.Move(logFile, oldLog, overwrite: true);
                    }

                    string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";
                    System.IO.File.AppendAllText(logFile, line + Environment.NewLine);
                }
            }
            catch (Exception logEx) when (logEx is System.IO.IOException or UnauthorizedAccessException or SecurityException)
            {
                // Defensive fallback: logging failure swallowed silently to avoid crash loop
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            this.DispatcherUnhandledException += (s, args) =>
            {
                var ex = args.Exception;
                if (ex is OutOfMemoryException or StackOverflowException or AccessViolationException)
                {
                    return; // Fatal: let runtime terminate cleanly
                }

                LogError("ERROR", $"UI Exception ({ex.GetType().Name}): {ex.Message}\n{ex.StackTrace}");
                args.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                LogError("FATAL", $"Domain Exception: {args.ExceptionObject}");
            };

            base.OnStartup(e);
            // Least privilege: runs normally for standard user; elevation requested only per-action
        }
    }
}
