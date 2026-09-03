using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Tempo.Services
{
    public class StorageDriveInfo
    {
        public string DriveLetter { get; set; } = "";
        public string VolumeLabel { get; set; } = "";
        public double TotalGb { get; set; }
        public double FreeGb { get; set; }
        public double UsedGb { get; set; }
        public double UsedPercent { get; set; }
        public string MediaType { get; set; } = "SSD"; // SSD, HDD, or Storage
        public bool IsSsd => MediaType.Equals("SSD", StringComparison.OrdinalIgnoreCase);
    }

    public class StartupAppItem
    {
        public string Name { get; set; } = "";
        public string Command { get; set; } = "";
        public string Location { get; set; } = "";
        public bool IsEnabled { get; set; } = true;
        public ImageSource? IconSource { get; set; }
        public bool IsUserScope => Location.Contains("المستخدم", StringComparison.OrdinalIgnoreCase) || Location.Contains("HKCU", StringComparison.OrdinalIgnoreCase);
        public string ActionText => IsUserScope 
            ? (LocalizationManager.CurrentLanguage == "ar" ? "تعطيل" : "Disable") 
            : (LocalizationManager.CurrentLanguage == "ar" ? "إدارة" : "Manage");

        public string LocationFriendly => IsUserScope 
            ? (LocalizationManager.CurrentLanguage == "ar" ? "المستخدم الحالي (HKCU)" : "Current User (HKCU)") 
            : (LocalizationManager.CurrentLanguage == "ar" ? "النظام (HKLM 64-bit)" : "System (HKLM 64-bit)");

        public string CleanExecutablePath
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Command)) return "";
                string c = Command.Trim();
                int exeIdx = c.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                string path = exeIdx > 0 ? c.Substring(0, exeIdx + 4).Trim('\"', ' ') : c.Trim('\"');
                return Environment.ExpandEnvironmentVariables(path);
            }
        }

        public string TooltipText => (LocalizationManager.CurrentLanguage == "ar")
            ? $"اسم البرنامج: {DisplayName}\n" +
              $"المعرف في السجل: {Name}\n" +
              $"المسار التنفيذي: {CleanExecutablePath}\n" +
              $"النطاق: {LocationFriendly}" +
              (IsSecurityApp ? "\n\n⚠️ ملاحظة: هذا التطبيق أساسي لأمان وحماية النظام، ينصح بعدم تعطيله." : "")
            : $"Program: {DisplayName}\n" +
              $"Registry Key: {Name}\n" +
              $"Executable Path: {CleanExecutablePath}\n" +
              $"Scope: {LocationFriendly}" +
              (IsSecurityApp ? "\n\n⚠️ Safety Notice: This service is vital for system protection. Disabling is not recommended." : "");

        // Security App Detection (Point 5)
        public bool IsSecurityApp =>
            Name.IndexOf("Security", StringComparison.OrdinalIgnoreCase) >= 0 ||
            Name.IndexOf("Defender", StringComparison.OrdinalIgnoreCase) >= 0 ||
            Name.IndexOf("Antivirus", StringComparison.OrdinalIgnoreCase) >= 0 ||
            Name.IndexOf("Avast", StringComparison.OrdinalIgnoreCase) >= 0 ||
            Name.IndexOf("Kaspersky", StringComparison.OrdinalIgnoreCase) >= 0 ||
            Name.IndexOf("Bitdefender", StringComparison.OrdinalIgnoreCase) >= 0 ||
            Name.IndexOf("Malware", StringComparison.OrdinalIgnoreCase) >= 0 ||
            Name.IndexOf("ESET", StringComparison.OrdinalIgnoreCase) >= 0 ||
            Name.IndexOf("Norton", StringComparison.OrdinalIgnoreCase) >= 0 ||
            Name.IndexOf("Firewall", StringComparison.OrdinalIgnoreCase) >= 0 ||
            Command.IndexOf("SecurityHealth", StringComparison.OrdinalIgnoreCase) >= 0;

        public Visibility SecurityBadgeVisibility => IsSecurityApp ? Visibility.Visible : Visibility.Collapsed;

        // Simplified, Friendly Name for Clean UI
        public string DisplayName
        {
            get
            {
                if (Name.StartsWith("MicrosoftEdgeAutoLaunch_", StringComparison.OrdinalIgnoreCase))
                    return "Microsoft Edge";
                if (Name.Equals("GoogleDriveFS", StringComparison.OrdinalIgnoreCase))
                    return "Google Drive";
                if (Name.Equals("Microsoft.Lists", StringComparison.OrdinalIgnoreCase))
                    return "Microsoft Lists";
                if (Name.Equals("Claude", StringComparison.OrdinalIgnoreCase))
                    return "Claude Desktop";
                if (Name.Equals("OneDrive", StringComparison.OrdinalIgnoreCase))
                    return "Microsoft OneDrive";
                if (Name.Equals("Docker Desktop", StringComparison.OrdinalIgnoreCase))
                    return "Docker Desktop";
                if (Name.Equals("SecurityHealth", StringComparison.OrdinalIgnoreCase))
                    return (LocalizationManager.CurrentLanguage == "ar") ? "أمان ويندوز (Windows Security)" : "Windows Security";
                if (Name.Equals("Avast Driver Updater UI", StringComparison.OrdinalIgnoreCase))
                    return "Avast Driver Updater";
                if (Name.Equals("MTPW", StringComparison.OrdinalIgnoreCase))
                    return "MiniTool Partition Wizard Update";
                if (Name.Equals("UrbanVPN", StringComparison.OrdinalIgnoreCase))
                    return "Urban VPN";
                if (Name.StartsWith("{") && Name.EndsWith("}"))
                    return (LocalizationManager.CurrentLanguage == "ar") ? "برنامج غير معروف (معرف نظام)" : "Unknown Program (System GUID)";
                if (Name.Length > 24 && System.Text.RegularExpressions.Regex.IsMatch(Name, @"[0-9a-fA-F]{16,}"))
                    return (LocalizationManager.CurrentLanguage == "ar") ? "أداة خلفية للنظام" : "Background System Service";
                return Name;
            }
        }

        public string TechnicalDetails => "";
        public Visibility TechnicalVisibility => Visibility.Collapsed;
    }

    public class HardwareMonitorService : IDisposable
    {
        private readonly Computer _computer;
        private PerformanceCounter? _cpuCounter;
        private long _lastNetRecvBytes = -1;
        private long _lastNetSentBytes = -1;
        private DateTime _lastNetTime = DateTime.MinValue;
        private readonly object _netLock = new();

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;

            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        private volatile bool _isComputerOpen = false;

        public HardwareMonitorService()
        {
            // Only enable CPU, GPU, Memory in LibreHardwareMonitor.
            // Storage is handled via lightweight Win32/DriveInfo/WMI — avoid slow SMART controller bus initialization.
            // Motherboard SuperIO bus is slow and unnecessary for CPU/RAM/GPU widgets.
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsStorageEnabled = false,
                IsMotherboardEnabled = false
            };

            // Asynchronous init of CPU PerformanceCounter and LibreHardwareMonitor
            // so UI loads immediately (<50ms) without any main thread registry stalls
            Task.Run(() =>
            {
                try
                {
                    _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                    _cpuCounter.NextValue();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("CPU PerformanceCounter notice: " + ex.Message);
                }

                try
                {
                    _computer.Open();
                    _isComputerOpen = true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("LibreHardwareMonitor Open notice: " + ex.Message);
                }
            });
        }


        public (float cpuPercent, float? cpuTemp, float cpuClockGhz) GetCpuMetrics()
        {
            float cpuPercent = 0f;
            try { cpuPercent = _cpuCounter?.NextValue() ?? 0f; } catch { }

            float? cpuTemp = null;
            float cpuClockMhz = 2600f;

            if (!_isComputerOpen)
            {
                return (cpuPercent, null, cpuClockMhz / 1000f);
            }

            try
            {
                foreach (var hardware in _computer.Hardware)
                {
                    if (hardware.HardwareType == HardwareType.Cpu)
                    {
                        hardware.Update();
                        foreach (var sensor in hardware.Sensors)
                        {
                            if (sensor.SensorType == SensorType.Temperature && sensor.Name.Contains("Core (Average)", StringComparison.OrdinalIgnoreCase))
                            {
                                cpuTemp = sensor.Value;
                            }
                            else if (sensor.SensorType == SensorType.Temperature && cpuTemp == null && sensor.Name.Contains("Core #1", StringComparison.OrdinalIgnoreCase))
                            {
                                cpuTemp = sensor.Value;
                            }
                            else if (sensor.SensorType == SensorType.Clock && sensor.Name.Contains("Core #1", StringComparison.OrdinalIgnoreCase))
                            {
                                if (sensor.Value.HasValue) cpuClockMhz = sensor.Value.Value;
                            }
                        }
                    }
                }
            }
            catch { }

            return (cpuPercent, cpuTemp, cpuClockMhz / 1000f);
        }

        public (double totalGb, double usedGb, double freeGb, double percent) GetRamMetrics()
        {
            var memStatus = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(memStatus))
            {
                double totalGb = Math.Round((double)memStatus.ullTotalPhys / (1024 * 1024 * 1024), 2);
                double freeGb = Math.Round((double)memStatus.ullAvailPhys / (1024 * 1024 * 1024), 2);
                double usedGb = Math.Round(totalGb - freeGb, 2);
                double percent = memStatus.dwMemoryLoad;
                return (totalGb, usedGb, freeGb, percent);
            }
            return (0, 0, 0, 0);
        }

        public (string name, float? temp, float? utilization, float vramUsedMb, float vramTotalMb) GetNvidiaGpuMetrics()
        {
            if (!_isComputerOpen)
            {
                return ("NVIDIA GPU", null, null, 0, 0);
            }

            try
            {
                foreach (var hardware in _computer.Hardware)
                {
                    if (hardware.HardwareType == HardwareType.GpuNvidia)
                    {
                        hardware.Update();
                        float? temp = null;
                        float? load = null;
                        float vramUsed = 0f;
                        float vramTotal = 4096f;

                        foreach (var sensor in hardware.Sensors)
                        {
                            if (sensor.SensorType == SensorType.Temperature && sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                                temp = sensor.Value;
                            else if (sensor.SensorType == SensorType.Load && sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                                load = sensor.Value;
                            else if (sensor.SensorType == SensorType.SmallData && sensor.Name.Contains("Memory Used", StringComparison.OrdinalIgnoreCase))
                                vramUsed = sensor.Value ?? 0f;
                            else if (sensor.SensorType == SensorType.SmallData && sensor.Name.Contains("Memory Total", StringComparison.OrdinalIgnoreCase))
                                vramTotal = sensor.Value ?? 4096f;
                        }
                        return (hardware.Name, temp, load, vramUsed, vramTotal);
                    }
                }
            }
            catch { }

            return (LocalizationManager.CurrentLanguage == "ar" ? "لا يوجد كارت منفصل" : "Integrated / No dedicated GPU", null, null, 0, 0);
        }

        public (double downKbSec, double upKbSec, string downFormatted, string upFormatted) GetNetworkMetrics()
        {
            try
            {
                long currentRecv = 0;
                long currentSent = 0;
                DateTime now = DateTime.UtcNow;

                var nics = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var nic in nics)
                {
                    if (nic.OperationalStatus == OperationalStatus.Up &&
                        nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    {
                        try
                        {
                            var stats = nic.GetIPStatistics();
                            currentRecv += stats.BytesReceived;
                            currentSent += stats.BytesSent;
                        }
                        catch { }
                    }
                }

                double downKb = 0;
                double upKb = 0;

                lock (_netLock)
                {
                    if (_lastNetRecvBytes >= 0 && _lastNetTime != DateTime.MinValue)
                    {
                        double elapsedSec = (now - _lastNetTime).TotalSeconds;
                        if (elapsedSec > 0.05)
                        {
                            long deltaRecv = currentRecv - _lastNetRecvBytes;
                            long deltaSent = currentSent - _lastNetSentBytes;

                            if (deltaRecv >= 0) downKb = (deltaRecv / 1024.0) / elapsedSec;
                            if (deltaSent >= 0) upKb = (deltaSent / 1024.0) / elapsedSec;
                        }
                    }

                    _lastNetRecvBytes = currentRecv;
                    _lastNetSentBytes = currentSent;
                    _lastNetTime = now;
                }

                string downStr = downKb >= 1024 ? $"{downKb / 1024.0:F1} MB/s" : $"{downKb:F1} KB/s";
                string upStr = upKb >= 1024 ? $"{upKb / 1024.0:F1} MB/s" : $"{upKb:F1} KB/s";

                return (downKb, upKb, downStr, upStr);
            }
            catch
            {
                return (0, 0, "0.0 KB/s", "0.0 KB/s");
            }
        }

        public List<StorageDriveInfo> GetStorageMetricsWithDriveType()
        {
            var list = new List<StorageDriveInfo>();

            // Build DeviceId → MediaType map for all physical disks
            // MediaType: 3=HDD, 4=SSD, 5=SCM/Optane, 0=Unknown
            // Then map partitions → drive letters via MSFT_Partition
            var diskMediaMap = new Dictionary<string, string>(); // DriveLetter → MediaType string
            var diskTypes = new Dictionary<string, string>(); // DeviceId → "SSD"/"HDD"/"Unknown"
            string singleDefaultType = "";

            try
            {
                var diskSearcher = new ManagementObjectSearcher(
                    @"root\Microsoft\Windows\Storage",
                    "SELECT DeviceId, MediaType FROM MSFT_PhysicalDisk");

                foreach (ManagementObject disk in diskSearcher.Get())
                {
                    string devId = disk["DeviceId"]?.ToString() ?? "";
                    ushort mType = disk["MediaType"] != null ? Convert.ToUInt16(disk["MediaType"]) : (ushort)0;
                    string typeStr = mType == 4 || mType == 5 ? "SSD" : mType == 3 ? "HDD" : "SSD";
                    diskTypes[devId] = typeStr;
                    singleDefaultType = typeStr;
                }

                // Map partitions to their drive letters via DiskNumber
                var partSearcher = new ManagementObjectSearcher(
                    @"root\Microsoft\Windows\Storage",
                    "SELECT DiskNumber, DriveLetter FROM MSFT_Partition WHERE DriveLetter IS NOT NULL");

                foreach (ManagementObject part in partSearcher.Get())
                {
                    string diskNum = part["DiskNumber"]?.ToString() ?? "";
                    string driveLetter = part["DriveLetter"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(driveLetter) && diskTypes.TryGetValue(diskNum, out string? mediaType))
                    {
                        diskMediaMap[driveLetter.TrimEnd(':').ToUpperInvariant()] = mediaType!;
                    }
                }
            }
            catch { }

            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
                    continue;

                try
                {
                    double total = Math.Round((double)drive.TotalSize / (1024 * 1024 * 1024), 1);
                    double free = Math.Round((double)drive.AvailableFreeSpace / (1024 * 1024 * 1024), 1);
                    double used = Math.Round(total - free, 1);
                    double percent = total > 0 ? Math.Round((used / total) * 100, 1) : 0;

                    string driveLetter = drive.Name.TrimEnd('\\').TrimEnd(':').ToUpperInvariant();
                    string mediaType = "SSD";
                    if (diskMediaMap.TryGetValue(driveLetter, out string? mt) && !string.IsNullOrEmpty(mt))
                    {
                        mediaType = mt;
                    }
                    else if (diskTypes.Count == 1 && !string.IsNullOrEmpty(singleDefaultType))
                    {
                        mediaType = singleDefaultType;
                    }

                    list.Add(new StorageDriveInfo
                    {
                        DriveLetter = drive.Name.TrimEnd('\\'),
                        VolumeLabel = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? (LocalizationManager.CurrentLanguage == "ar" ? "قرص محلي" : "Local Disk") : drive.VolumeLabel,
                        TotalGb = total,
                        FreeGb = free,
                        UsedGb = used,
                        UsedPercent = percent,
                        MediaType = mediaType
                    });
                }
                catch { }
            }

            return list;
        }

        public (string processName, double ramMb)[] GetTop5RamProcesses()
        {
            return Process.GetProcesses()
                .Select(p =>
                {
                    try
                    {
                        return new { Name = p.ProcessName, WorkingSetMb = p.WorkingSet64 / (1024.0 * 1024.0) };
                    }
                    catch
                    {
                        return new { Name = "", WorkingSetMb = 0.0 };
                    }
                })
                .Where(p => !string.IsNullOrEmpty(p.Name))
                .GroupBy(p => p.Name)
                .Select(g => (processName: g.Key, ramMb: Math.Round(g.Sum(x => x.WorkingSetMb), 1)))
                .OrderByDescending(x => x.ramMb)
                .Take(5)
                .ToArray();
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        public static ImageSource? ExtractIconFromCommand(string command)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(command)) return null;
                string c = command.Trim();
                int exeIdx = c.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                string cleanPath = exeIdx > 0 ? c.Substring(0, exeIdx + 4).Trim('\"', ' ') : c.Trim('\"');
                cleanPath = Environment.ExpandEnvironmentVariables(cleanPath);

                if (File.Exists(cleanPath))
                {
                    using var sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(cleanPath);
                    if (sysIcon != null)
                    {
                        IntPtr hIcon = sysIcon.Handle;
                        var bmp = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                            hIcon,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                        bmp.Freeze();
                        return bmp;
                    }
                }
            }
            catch { }
            return null;
        }

        public List<StartupAppItem> GetStartupApps()
        {
            var apps = new List<StartupAppItem>();
            const string runPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

            void ReadKey(RegistryHive hive, RegistryView view, string loc)
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var key = baseKey.OpenSubKey(runPath);
                    if (key == null) return;

                    foreach (var valueName in key.GetValueNames())
                    {
                        if (string.IsNullOrWhiteSpace(valueName)) continue;
                        if (apps.Any(a => a.Name.Equals(valueName, StringComparison.OrdinalIgnoreCase))) continue;

                        string cmd = key.GetValue(valueName)?.ToString() ?? "";

                        // Robust executable path extraction and validation
                        string exePath = "";
                        bool pathExists = false;
                        try
                        {
                            string c = cmd.Trim();
                            int exeIdx = c.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                            exePath = exeIdx > 0 ? c.Substring(0, exeIdx + 4).Trim('\"', ' ') : c.Trim('\"');
                            exePath = Environment.ExpandEnvironmentVariables(exePath);
                            pathExists = !string.IsNullOrEmpty(exePath) && File.Exists(exePath);
                        }
                        catch { }

                        var icon = ExtractIconFromCommand(cmd);

                        apps.Add(new StartupAppItem
                        {
                            Name = valueName,
                            Command = cmd,
                            Location = loc,
                            IsEnabled = pathExists,
                            IconSource = icon
                        });
                    }
                }
                catch { }
            }

            ReadKey(RegistryHive.CurrentUser, RegistryView.Default, "HKCU");
            ReadKey(RegistryHive.LocalMachine, RegistryView.Registry64, "HKLM-64");
            ReadKey(RegistryHive.LocalMachine, RegistryView.Registry32, "HKLM-32");

            return apps;
        }

        public bool DisableStartupApp(StartupAppItem app)
        {
            try
            {
                if (app.IsUserScope)
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                    if (key != null && key.GetValue(app.Name) != null)
                    {
                        key.DeleteValue(app.Name, false);
                        return true;
                    }
                }
                else
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                    if (key != null && key.GetValue(app.Name) != null)
                    {
                        key.DeleteValue(app.Name, false);
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        public (string displayName, bool isActive) GetWindowsSecurityStatus()
        {
            try
            {
                var searcher = new ManagementObjectSearcher(@"root\SecurityCenter2", "SELECT displayName, productState FROM AntiVirusProduct");
                var results = searcher.Get();
                var items = new List<(string name, bool active)>();

                foreach (ManagementObject obj in results)
                {
                    string name = obj["displayName"]?.ToString() ?? "Unknown AV";
                    uint state = obj["productState"] != null ? Convert.ToUInt32(obj["productState"]) : 0;
                    // productState bits: bits 12-15 = active state (0x1000), bits 4-7 = up-to-date
                    // Common active states: 397568 (0x61100), 393472 (0x60100), 266240 (0x41000)
                    bool active = ((state >> 12) & 0xF) == 1;
                    items.Add((name, active));
                }

                if (items.Count == 0)
                    return (LocalizationManager.CurrentLanguage == "ar" ? "لا يوجد برنامج حماية مُعرَّف" : "No Antivirus Detected", false);

                // Prefer first active AV; if none active, return first with inactive status
                var activeAv = items.FirstOrDefault(i => i.active);
                if (activeAv.name != null)
                    return (activeAv.name, true);

                return (items[0].name, false);
            }
            catch (ManagementException)
            {
                // SecurityCenter2 unavailable (e.g. Server OS, permission denied)
                return (LocalizationManager.CurrentLanguage == "ar" ? "Security Center غير متاح" : "Security Center Unavailable", false);
            }
            catch
            {
                return (LocalizationManager.CurrentLanguage == "ar" ? "حالة الحماية: غير معروفة" : "Protection Status: Unknown", false);
            }
        }

        public void Dispose()
        {
            try { _cpuCounter?.Dispose(); } catch { }
            if (_isComputerOpen && _computer != null)
            {
                Task.Run(() => {
                    try { _computer?.Close(); } catch { }
                });
            }
        }
    }
}
