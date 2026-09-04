using System;
using System.Security.Principal;
using System.Windows;

namespace Tempo
{
    public partial class App : System.Windows.Application
    {
        public static bool IsAdmin { get; private set; } = false;

        protected override void OnStartup(StartupEventArgs e)
        {
            this.DispatcherUnhandledException += (s, args) =>
            {
                var ex = args.Exception;
                if (ex is OutOfMemoryException or StackOverflowException or AccessViolationException)
                {
                    return; // Fatal: let runtime terminate cleanly
                }

                try
                {
                    string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tempo");
                    if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                    System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "error.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] UI Exception ({ex.GetType().Name}): {ex.Message}\n{ex.StackTrace}\n\n");
                }
                catch { }
                args.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                try
                {
                    string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tempo");
                    if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                    System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "error.log"), $"[{DateTime.Now}] Domain Exception: {args.ExceptionObject}\n");
                }
                catch { }
            };

            base.OnStartup(e);
            IsAdmin = IsRunningAsAdministrator();
            // Least privilege: runs normally for standard user; elevation requested only per-action
        }

        private static bool IsRunningAsAdministrator()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }
}
