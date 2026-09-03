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
