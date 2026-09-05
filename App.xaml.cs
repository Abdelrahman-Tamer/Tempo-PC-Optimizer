using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Management;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Xml;
using Microsoft.Win32;
using Tempo.Models;
using Tempo.Services;

namespace Tempo
{
    public partial class App : System.Windows.Application
    {
        private static readonly object _logLock = new();
        private const long MaxLogSizeBytes = 5 * 1024 * 1024; // 5 MB rotation threshold

        public static string GetOwnExePath()
        {
            string? path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                return path;

            try
            {
                path = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    return path;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
            }

            path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tempo.exe");
            return path;
        }

        public static bool IsAdministrator()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return false;
            }
        }

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

        private static void HandleElevatedChildCommand(string[] args)
        {
            if (!IsAdministrator())
            {
                Environment.Exit(10); // Distinct exit code: Not Administrator
            }

            string command = args[0];
            if (command.Equals("--set-approved", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length != 4)
                {
                    Environment.Exit(11); // Invalid argument count
                }

                try
                {
                    string subPath = Encoding.UTF8.GetString(Convert.FromBase64String(args[1]));
                    string appName = Encoding.UTF8.GetString(Convert.FromBase64String(args[2]));
                    byte[] bytes = Convert.FromBase64String(args[3]);

                    if (string.IsNullOrWhiteSpace(appName) || appName.Any(char.IsControl))
                    {
                        Environment.Exit(12); // Control character / invalid appName rejected
                    }

                    if (bytes == null || bytes.Length != 12)
                    {
                        Environment.Exit(12); // Invalid bytes length
                    }

                    if (bytes[0] == 0x06)
                    {
                        Environment.Exit(12); // Cannot forge or force system-managed status
                    }

                    var allowedSubKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run",
                        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32",
                        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder"
                    };

                    if (!allowedSubKeys.Contains(subPath))
                    {
                        Environment.Exit(12); // SubPath not in allowlist
                    }

                    using var hklm64 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                    using var existingKey = hklm64.OpenSubKey(subPath, false);
                    if (existingKey != null)
                    {
                        var existingVal = existingKey.GetValue(appName);
                        if (existingVal is byte[] existingBytes && existingBytes.Length > 0 && existingBytes[0] == 0x06)
                        {
                            Environment.Exit(12); // System managed app protected from modification
                        }
                    }

                    using var key = hklm64.CreateSubKey(subPath, true);
                    if (key != null)
                    {
                        key.SetValue(appName, bytes, RegistryValueKind.Binary);
                        Environment.Exit(0);
                    }
                    Environment.Exit(13);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    Environment.Exit(13);
                }
            }
            else if (command.Equals("--measure-boot", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    ExecuteElevatedBootMeasure();
                    Environment.Exit(0);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    Environment.Exit(20);
                }
            }
            else if (command.Equals("--trim", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length != 2)
                {
                    Environment.Exit(11); // Invalid argument count
                }

                string letter = args[1].Trim().TrimEnd(':', '\\', '/').ToUpperInvariant();
                if (letter.Length != 1 || letter[0] < 'A' || letter[0] > 'Z')
                {
                    Environment.Exit(12); // Invalid drive letter
                }

                try
                {
                    var dInfo = new DriveInfo(letter);
                    if (dInfo.DriveType != DriveType.Fixed)
                    {
                        Environment.Exit(12); // Non-fixed drive rejected
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    Environment.Exit(12);
                }

                try
                {
                    string defragExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "defrag.exe");
                    if (!File.Exists(defragExe))
                    {
                        Environment.Exit(13);
                    }

                    var defragPsi = new ProcessStartInfo
                    {
                        FileName = defragExe,
                        Arguments = $"{letter}: /L",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var proc = Process.Start(defragPsi);
                    if (proc == null)
                    {
                        Environment.Exit(13);
                    }

                    bool exited = proc.WaitForExit(30000);
                    if (!exited)
                    {
                        try { proc.Kill(entireProcessTree: true); } catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
                        Environment.Exit(14); // 30s timeout watchdog triggered
                    }

                    Environment.Exit(proc.ExitCode == 0 ? 0 : proc.ExitCode);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    Environment.Exit(13);
                }
            }

            Environment.Exit(1);
        }

        private static string? ExtractXmlValue(string xml, string fieldName)
        {
            string marker1 = $"Name='{fieldName}'";
            string marker2 = $"Name=\"{fieldName}\"";
            int idx = xml.IndexOf(marker1, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) idx = xml.IndexOf(marker2, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                int s = xml.IndexOf('>', idx);
                int e = xml.IndexOf('<', s);
                if (s >= 0 && e > s)
                {
                    return xml.Substring(s + 1, e - s - 1);
                }
            }
            return null;
        }

        private static void ExecuteElevatedBootMeasure()
        {
            long mainPathMs = 0;
            try
            {
                var query = new EventLogQuery("Microsoft-Windows-Diagnostics-Performance/Operational", PathType.LogName, "*[System[(EventID=100)]]")
                {
                    ReverseDirection = true
                };
                using var reader = new EventLogReader(query);
                EventRecord? rec = reader.ReadEvent();
                if (rec != null)
                {
                    using (rec)
                    {
                        string xml = rec.ToXml();
                        int idx = xml.IndexOf("MainPathBootTime", StringComparison.OrdinalIgnoreCase);
                        if (idx >= 0)
                        {
                            int s = xml.IndexOf('>', idx);
                            int e = xml.IndexOf('<', s);
                            if (s >= 0 && e > s)
                            {
                                string val = xml.Substring(s + 1, e - s - 1);
                                long.TryParse(val, out mainPathMs);
                            }
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
            }

            var apps = new List<BootMeasureAppItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                string sysRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string wdiDir = Path.Combine(sysRoot, "System32", "wdi", "LogFiles", "StartupInfo");
                if (Directory.Exists(wdiDir))
                {
                    var di = new DirectoryInfo(wdiDir);
                    var xmlFiles = di.GetFiles("*.xml").OrderByDescending(f => f.LastWriteTimeUtc).ToList();
                    if (xmlFiles.Count > 0)
                    {
                        var doc = new XmlDocument();
                        doc.Load(xmlFiles[0].FullName);
                        var procNodes = doc.SelectNodes("//process");
                        if (procNodes != null)
                        {
                            foreach (XmlNode pNode in procNodes)
                            {
                                string? pNameRaw = pNode.Attributes?["name"]?.Value;
                                if (string.IsNullOrWhiteSpace(pNameRaw)) continue;

                                string pName = HardwareMonitorService.ExtractProcessBaseName(pNameRaw, pNameRaw);
                                if (!string.IsNullOrEmpty(pName) && seen.Add(pName))
                                {
                                    long cpu = 0;
                                    long disk = 0;
                                    string? cpuAttr = pNode.Attributes?["cpuTimeMs"]?.Value;
                                    if (!string.IsNullOrEmpty(cpuAttr)) long.TryParse(cpuAttr, out cpu);

                                    string? diskAttr = pNode.Attributes?["diskBytes"]?.Value ?? pNode.Attributes?["diskBytesTotal"]?.Value;
                                    if (!string.IsNullOrEmpty(diskAttr)) long.TryParse(diskAttr, out disk);

                                    apps.Add(new BootMeasureAppItem
                                    {
                                        ProcessName = pName,
                                        CpuMs = cpu,
                                        DiskBytes = disk
                                    });
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
            }

            try
            {
                var query101 = new EventLogQuery("Microsoft-Windows-Diagnostics-Performance/Operational", PathType.LogName, "*[System[(EventID=101)]]")
                {
                    ReverseDirection = true
                };
                using var reader101 = new EventLogReader(query101);
                for (int i = 0; i < 50; i++)
                {
                    EventRecord? rec = reader101.ReadEvent();
                    if (rec == null) break;
                    using (rec)
                    {
                        string xml = rec.ToXml();
                        string? nameData = ExtractXmlValue(xml, "Name") ?? ExtractXmlValue(xml, "AppPath");
                        if (!string.IsNullOrWhiteSpace(nameData))
                        {
                            string pName = HardwareMonitorService.ExtractProcessBaseName(nameData, nameData);
                            if (!string.IsNullOrEmpty(pName) && seen.Add(pName))
                            {
                                long cpu = 0;
                                long disk = 0;
                                string? cpuData = ExtractXmlValue(xml, "TotalTime") ?? ExtractXmlValue(xml, "CpuTime");
                                if (!string.IsNullOrEmpty(cpuData)) long.TryParse(cpuData, out cpu);

                                string? diskData = ExtractXmlValue(xml, "DiskBytes") ?? ExtractXmlValue(xml, "TotalDiskBytes");
                                if (!string.IsNullOrEmpty(diskData)) long.TryParse(diskData, out disk);

                                apps.Add(new BootMeasureAppItem
                                {
                                    ProcessName = pName,
                                    CpuMs = cpu,
                                    DiskBytes = disk
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
            }

            string bootId = "";
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT LastBootUpTime FROM Win32_OperatingSystem");
                using var coll = searcher.Get();
                foreach (ManagementObject mo in coll)
                {
                    using (mo)
                    {
                        if (mo["LastBootUpTime"] is string bStr)
                        {
                            bootId = ManagementDateTimeConverter.ToDateTime(bStr).ToUniversalTime().ToString("o");
                            break;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
            }

            var cacheObj = new BootMeasureCache
            {
                Timestamp = DateTime.UtcNow,
                BootId = bootId,
                MainPathBootMs = mainPathMs,
                Apps = apps
            };

            string json = JsonSerializer.Serialize(cacheObj, new JsonSerializerOptions { WriteIndented = true });

            string progDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Tempo");
            string appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tempo");
            string targetFile = Path.Combine(progDataDir, "boot-measure.json");

            try
            {
                if (!Directory.Exists(progDataDir)) Directory.CreateDirectory(progDataDir);
                File.WriteAllText(targetFile, json, Encoding.UTF8);

                var fileInfo = new FileInfo(targetFile);
                var fileSecurity = fileInfo.GetAccessControl();
                fileSecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

                var adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                var usersSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);

                fileSecurity.ResetAccessRule(new FileSystemAccessRule(adminSid, FileSystemRights.FullControl, AccessControlType.Allow));
                fileSecurity.AddAccessRule(new FileSystemAccessRule(usersSid, FileSystemRights.ReadAndExecute, AccessControlType.Allow));
                fileInfo.SetAccessControl(fileSecurity);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                if (!Directory.Exists(appDataDir)) Directory.CreateDirectory(appDataDir);
                targetFile = Path.Combine(appDataDir, "boot-measure.json");
                File.WriteAllText(targetFile, json, Encoding.UTF8);
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            if (e.Args.Length > 0)
            {
                string firstArg = e.Args[0];
                if (firstArg.Equals("--set-approved", StringComparison.OrdinalIgnoreCase) ||
                    firstArg.Equals("--measure-boot", StringComparison.OrdinalIgnoreCase) ||
                    firstArg.Equals("--trim", StringComparison.OrdinalIgnoreCase))
                {
                    HandleElevatedChildCommand(e.Args);
                    return;
                }
            }

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
