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

    public enum StartupImpactLevel
    {
        Disabled,
        Low,
        Medium,
        High
    }

    public class BootPerformanceInfo
    {
        public double BiosTimeSeconds { get; set; }
        public double EstimatedTotalBootSeconds { get; set; }
        public int ActiveStartupAppsCount { get; set; }
        public int DisabledStartupAppsCount { get; set; }
        public double ActiveAppsDelaySeconds { get; set; }
        public string Rating { get; set; } = "Fast";
        public string RatingText { get; set; } = "";
        public string Recommendation { get; set; } = "";
    }

    public class StartupAppItem
    {
        public string Name { get; set; } = "";
        public string Command { get; set; } = "";
        public string Location { get; set; } = "";
        public bool IsEnabled { get; set; } = true;
        public StartupImpactLevel Impact { get; set; } = StartupImpactLevel.Low;
        public ImageSource? IconSource { get; set; }
        public bool IsUserScope => Location.Contains("المستخدم", StringComparison.OrdinalIgnoreCase) || Location.Contains("HKCU", StringComparison.OrdinalIgnoreCase);

        public Brush StatusDotBrush => IsEnabled
            ? new SolidColorBrush(Color.FromRgb(16, 185, 129)) // TealHealth
            : new SolidColorBrush(Color.FromRgb(100, 116, 139)); // Slate Muted

        public string StatusLabel => IsEnabled
            ? (LocalizationManager.CurrentLanguage == "ar" ? "مفعّل" : "Enabled")
            : (LocalizationManager.CurrentLanguage == "ar" ? "معطّل" : "Disabled");

        public string ImpactLabel => !IsEnabled
            ? (LocalizationManager.CurrentLanguage == "ar" ? "بدون أثر" : "No Impact")
            : Impact switch
            {
                StartupImpactLevel.High => (LocalizationManager.CurrentLanguage == "ar" ? "أثر مرتفع (~2s+)" : "High Impact (~2s+)"),
                StartupImpactLevel.Medium => (LocalizationManager.CurrentLanguage == "ar" ? "أثر متوسط (~1s)" : "Medium Impact (~1s)"),
                _ => (LocalizationManager.CurrentLanguage == "ar" ? "أثر خفيف (<0.5s)" : "Low Impact (<0.5s)")
            };

        public Brush ImpactBadgeBg => !IsEnabled
            ? new SolidColorBrush(Color.FromArgb(25, 100, 116, 139))
            : Impact switch
            {
                StartupImpactLevel.High => new SolidColorBrush(Color.FromArgb(35, 244, 63, 94)),
                StartupImpactLevel.Medium => new SolidColorBrush(Color.FromArgb(35, 245, 158, 11)),
                _ => new SolidColorBrush(Color.FromArgb(30, 16, 185, 129))
            };

        public Brush ImpactBadgeFg => !IsEnabled
            ? new SolidColorBrush(Color.FromRgb(148, 163, 184))
            : Impact switch
            {
                StartupImpactLevel.High => new SolidColorBrush(Color.FromRgb(244, 63, 94)),
                StartupImpactLevel.Medium => new SolidColorBrush(Color.FromRgb(245, 158, 11)),
                _ => new SolidColorBrush(Color.FromRgb(16, 185, 129))
            };

        public string ActionText => IsUserScope 
            ? (IsEnabled 
                ? (LocalizationManager.CurrentLanguage == "ar" ? "تعطيل" : "Disable") 
                : (LocalizationManager.CurrentLanguage == "ar" ? "تفعيل" : "Enable"))
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

        public string SecurityTooltipText => (LocalizationManager.CurrentLanguage == "ar")
            ? "خدمة أمنية - يُوصى بالإبقاء عليها قيد التشغيل"
            : "Security service - Recommended to keep enabled";

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


        private static readonly object _cpuLock = new();
        private static DateTime _lastCpuSampleTime = DateTime.MinValue;
        private static float _lastCpuPercent = 0f;
        private static float _detectedCpuBaseClockMhz = 0f;

        private static float GetBaseCpuClockMhz()
        {
            if (_detectedCpuBaseClockMhz > 0f) return _detectedCpuBaseClockMhz;

            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                if (key != null)
                {
                    var mhzObj = key.GetValue("~MHz");
                    if (mhzObj is int mhzInt && mhzInt > 500)
                    {
                        _detectedCpuBaseClockMhz = (float)mhzInt;
                        return _detectedCpuBaseClockMhz;
                    }
                }
            }
            catch { }

            _detectedCpuBaseClockMhz = 2600f;
            return _detectedCpuBaseClockMhz;
        }

        public (float cpuPercent, float? cpuTemp, float cpuClockGhz) GetCpuMetrics()
        {
            float cpuPercent = _lastCpuPercent;
            DateTime now = DateTime.UtcNow;

            lock (_cpuLock)
            {
                // Only sample PerformanceCounter if at least 400ms elapsed since last sample
                // to prevent skewed/elevated readings over tiny intervals
                if ((now - _lastCpuSampleTime).TotalMilliseconds >= 400 || _lastCpuSampleTime == DateTime.MinValue)
                {
                    try
                    {
                        if (_cpuCounter != null)
                        {
                            cpuPercent = _cpuCounter.NextValue();
                            cpuPercent = Math.Clamp(cpuPercent, 0f, 100f);
                            _lastCpuPercent = cpuPercent;
                            _lastCpuSampleTime = now;
                        }
                    }
                    catch { }
                }
            }

            float? cpuTemp = null;
            float cpuClockMhz = GetBaseCpuClockMhz();

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
                            else if (sensor.SensorType == SensorType.Clock && (sensor.Name.Contains("Core #1", StringComparison.OrdinalIgnoreCase) || sensor.Name.Contains("Core Max", StringComparison.OrdinalIgnoreCase)))
                            {
                                if (sensor.Value.HasValue && sensor.Value.Value > 100f) cpuClockMhz = sensor.Value.Value;
                            }
                            else if (sensor.SensorType == SensorType.Load && sensor.Name.Contains("CPU Total", StringComparison.OrdinalIgnoreCase))
                            {
                                if (cpuPercent <= 0.001f && sensor.Value.HasValue)
                                {
                                    cpuPercent = Math.Clamp(sensor.Value.Value, 0f, 100f);
                                }
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
                double percent = totalGb > 0 ? Math.Round(((totalGb - freeGb) / totalGb) * 100.0, 1) : 0;
                return (totalGb, usedGb, freeGb, percent);
            }
            return (0, 0, 0, 0);
        }

        public (string name, float? temp, float? utilization, float vramUsedMb, float vramTotalMb) GetGpuMetrics()
        {
            if (_isComputerOpen)
            {
                try
                {
                    IHardware? dedicatedGpu = null;
                    IHardware? integratedGpu = null;

                    foreach (var hw in _computer.Hardware)
                    {
                        if (hw.HardwareType == HardwareType.GpuNvidia)
                        {
                            dedicatedGpu = hw;
                            break;
                        }
                        else if (hw.HardwareType == HardwareType.GpuAmd && dedicatedGpu == null)
                        {
                            dedicatedGpu = hw;
                        }
                        else if (hw.HardwareType == HardwareType.GpuIntel)
                        {
                            if (hw.Name.Contains("Arc", StringComparison.OrdinalIgnoreCase))
                                dedicatedGpu = hw;
                            else if (integratedGpu == null)
                                integratedGpu = hw;
                        }
                    }

                    var targetGpu = dedicatedGpu ?? integratedGpu;
                    if (targetGpu != null)
                    {
                        targetGpu.Update();
                        float? temp = null;
                        float? load = null;
                        float vramUsed = 0f;
                        float vramTotal = 0f;

                        foreach (var sensor in targetGpu.Sensors)
                        {
                            if (sensor.SensorType == SensorType.Temperature &&
                                (sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) || sensor.Name.Contains("GPU", StringComparison.OrdinalIgnoreCase)))
                            {
                                temp ??= sensor.Value;
                            }
                            else if (sensor.SensorType == SensorType.Load &&
                                (sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) || sensor.Name.Contains("GPU", StringComparison.OrdinalIgnoreCase) || sensor.Name.Contains("D3D", StringComparison.OrdinalIgnoreCase)))
                            {
                                load ??= sensor.Value;
                            }
                            else if (sensor.SensorType == SensorType.SmallData && sensor.Name.Contains("Memory Used", StringComparison.OrdinalIgnoreCase))
                            {
                                vramUsed = sensor.Value ?? 0f;
                            }
                            else if (sensor.SensorType == SensorType.SmallData && sensor.Name.Contains("Memory Total", StringComparison.OrdinalIgnoreCase))
                            {
                                vramTotal = sensor.Value ?? 0f;
                            }
                        }
                        return (targetGpu.Name, temp, load, vramUsed, vramTotal);
                    }
                }
                catch { }
            }

            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
                foreach (ManagementObject mo in searcher.Get())
                {
                    string? name = mo["Name"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        return (name, null, null, 0, 0);
                    }
                }
            }
            catch { }

            string unavailable = (LocalizationManager.CurrentLanguage == "ar") ? "غير متاح" : "Unavailable";
            return (unavailable, null, null, 0, 0);
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

        private static readonly Dictionary<string, string> _cachedDiskMediaMap = new(StringComparer.OrdinalIgnoreCase);
        private static bool _diskMediaMapInitialized = false;
        private static readonly object _diskMediaLock = new();

        public List<StorageDriveInfo> GetStorageMetricsWithDriveType()
        {
            var list = new List<StorageDriveInfo>();

            lock (_diskMediaLock)
            {
                if (!_diskMediaMapInitialized)
                {
                    try
                    {
                        var diskTypes = new Dictionary<string, string>();
                        using var diskSearcher = new ManagementObjectSearcher(
                            @"root\Microsoft\Windows\Storage",
                            "SELECT DeviceId, MediaType FROM MSFT_PhysicalDisk");

                        foreach (ManagementObject disk in diskSearcher.Get())
                        {
                            using (disk)
                            {
                                string devId = disk["DeviceId"]?.ToString() ?? "";
                                ushort mType = disk["MediaType"] != null ? Convert.ToUInt16(disk["MediaType"]) : (ushort)0;
                                string typeStr = (mType == 4 || mType == 5) ? "SSD" : (mType == 3 ? "HDD" : "SSD");
                                diskTypes[devId] = typeStr;
                            }
                        }

                        using var partSearcher = new ManagementObjectSearcher(
                            @"root\Microsoft\Windows\Storage",
                            "SELECT DiskNumber, DriveLetter FROM MSFT_Partition WHERE DriveLetter IS NOT NULL");

                        foreach (ManagementObject part in partSearcher.Get())
                        {
                            using (part)
                            {
                                string diskNum = part["DiskNumber"]?.ToString() ?? "";
                                string driveLetter = part["DriveLetter"]?.ToString() ?? "";
                                if (!string.IsNullOrEmpty(driveLetter) && diskTypes.TryGetValue(diskNum, out string? mediaType))
                                {
                                    _cachedDiskMediaMap[driveLetter.TrimEnd(':').ToUpperInvariant()] = mediaType!;
                                }
                            }
                        }
                    }
                    catch { }
                    finally
                    {
                        _diskMediaMapInitialized = true;
                    }
                }
            }

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
                    if (_cachedDiskMediaMap.TryGetValue(driveLetter, out string? mt) && !string.IsNullOrEmpty(mt))
                    {
                        mediaType = mt;
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
            Process[] processes = Array.Empty<Process>();
            try
            {
                processes = Process.GetProcesses();
                return processes
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
            catch
            {
                return Array.Empty<(string, double)>();
            }
            finally
            {
                foreach (var p in processes)
                {
                    try { p.Dispose(); } catch { }
                }
            }
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

        public static StartupImpactLevel ClassifyImpact(string name, string cmd)
        {
            string target = (name + " " + cmd).ToLowerInvariant();
            
            // High Impact (Heavy apps, VPNs, cloud sync, gaming launchers, electron clients, updaters)
            if (target.Contains("vpn") || target.Contains("docker") || target.Contains("discord") ||
                target.Contains("teams") || target.Contains("slack") || target.Contains("steam") ||
                target.Contains("epic") || target.Contains("onedrive") || target.Contains("googledrive") ||
                target.Contains("dropbox") || target.Contains("chrome") || target.Contains("edge") ||
                target.Contains("brave") || target.Contains("adobe") || target.Contains("notion") ||
                target.Contains("grammarly") || target.Contains("update") || target.Contains("updater") ||
                target.Contains("java") || target.Contains("cisco") || target.Contains("torrent") ||
                target.Contains("download") || target.Contains("antivirus") || target.Contains("avast"))
            {
                return StartupImpactLevel.High;
            }

            // Medium Impact (Audio controllers, touchpad, hardware utilities, system helpers)
            if (target.Contains("audio") || target.Contains("realtek") || target.Contains("waves") ||
                target.Contains("synaptics") || target.Contains("touchpad") || target.Contains("intel") ||
                target.Contains("amd") || target.Contains("nvidia") || target.Contains("display") ||
                target.Contains("fences") || target.Contains("cleaner"))
            {
                return StartupImpactLevel.Medium;
            }

            // Low Impact (Lightweight monitors, simple tray items)
            return StartupImpactLevel.Low;
        }

        public List<StartupAppItem> GetStartupApps()
        {
            var apps = new List<StartupAppItem>();
            const string runPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
            const string approvedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
            const string approvedPath32 = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32";

            void ReadKey(RegistryHive hive, RegistryView view, string loc, string approvedSubPath)
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var key = baseKey.OpenSubKey(runPath);
                    if (key == null) return;

                    using var approvedKey = baseKey.OpenSubKey(approvedSubPath);

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

                        // Check Windows StartupApproved status:
                        // Byte 0x02 is Enabled in Windows Task Manager.
                        // Byte 0x03 or any non-02 means user disabled it in Task Manager.
                        bool isEnabledInWindows = true;
                        if (approvedKey != null)
                        {
                            var approvedVal = approvedKey.GetValue(valueName);
                            if (approvedVal is byte[] bytes && bytes.Length > 0)
                            {
                                isEnabledInWindows = (bytes[0] == 0x02);
                            }
                        }

                        bool isEnabled = pathExists && isEnabledInWindows;
                        var icon = ExtractIconFromCommand(cmd);
                        var baseImpact = ClassifyImpact(valueName, cmd);

                        apps.Add(new StartupAppItem
                        {
                            Name = valueName,
                            Command = cmd,
                            Location = loc,
                            IsEnabled = isEnabled,
                            Impact = isEnabled ? baseImpact : StartupImpactLevel.Disabled,
                            IconSource = icon
                        });
                    }
                }
                catch { }
            }

            ReadKey(RegistryHive.CurrentUser, RegistryView.Default, "HKCU", approvedPath);
            ReadKey(RegistryHive.LocalMachine, RegistryView.Registry64, "HKLM-64", approvedPath);
            ReadKey(RegistryHive.LocalMachine, RegistryView.Registry32, "HKLM-32", approvedPath32);

            return apps;
        }

        public bool ToggleStartupApp(StartupAppItem app)
        {
            try
            {
                if (app.IsUserScope)
                {
                    const string approvedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
                    using var approvedKey = Registry.CurrentUser.OpenSubKey(approvedPath, true);
                    if (approvedKey != null)
                    {
                        // 0x03 = Disabled, 0x02 = Enabled
                        byte[] newBytes = app.IsEnabled
                            ? new byte[] { 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }
                            : new byte[] { 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                        approvedKey.SetValue(app.Name, newBytes, RegistryValueKind.Binary);
                        app.IsEnabled = !app.IsEnabled;
                        app.Impact = app.IsEnabled ? ClassifyImpact(app.Name, app.Command) : StartupImpactLevel.Disabled;
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        public bool DisableStartupApp(StartupAppItem app)
        {
            return ToggleStartupApp(app);
        }

        public BootPerformanceInfo GetBootPerformanceInfo(List<StartupAppItem>? startupApps = null)
        {
            var info = new BootPerformanceInfo();
            try
            {
                using var pwrKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power");
                if (pwrKey != null)
                {
                    var fwVal = pwrKey.GetValue("FwPOSTTime");
                    if (fwVal is int fwInt && fwInt > 0)
                    {
                        info.BiosTimeSeconds = Math.Round(fwInt / 1000.0, 1);
                    }
                    else if (fwVal is long fwLong && fwLong > 0)
                    {
                        info.BiosTimeSeconds = Math.Round(fwLong / 1000.0, 1);
                    }
                }
            }
            catch { }

            if (info.BiosTimeSeconds <= 0)
            {
                info.BiosTimeSeconds = 11.6; // Baseline UEFI boot time
            }

            var apps = startupApps ?? GetStartupApps();
            info.ActiveStartupAppsCount = apps.Count(a => a.IsEnabled);
            info.DisabledStartupAppsCount = apps.Count(a => !a.IsEnabled);

            // Calculate active startup apps cumulative delay
            double activeDelay = 0;
            foreach (var app in apps.Where(a => a.IsEnabled))
            {
                activeDelay += app.Impact switch
                {
                    StartupImpactLevel.High => 1.8,
                    StartupImpactLevel.Medium => 0.8,
                    _ => 0.3
                };
            }
            info.ActiveAppsDelaySeconds = Math.Round(activeDelay, 1);

            // Estimated total boot time = BIOS + ~5.5s Kernel init + apps delay
            info.EstimatedTotalBootSeconds = Math.Round(info.BiosTimeSeconds + 5.5 + info.ActiveAppsDelaySeconds, 1);

            if (info.EstimatedTotalBootSeconds <= 20.0)
            {
                info.Rating = "Fast";
                info.RatingText = LocalizationManager.CurrentLanguage == "ar" ? "ممتاز وسريع" : "Fast & Optimal";
            }
            else if (info.EstimatedTotalBootSeconds <= 35.0)
            {
                info.Rating = "Moderate";
                info.RatingText = LocalizationManager.CurrentLanguage == "ar" ? "جيد ومقبول" : "Moderate";
            }
            else
            {
                info.Rating = "Slow";
                info.RatingText = LocalizationManager.CurrentLanguage == "ar" ? "يحتاج تحسين" : "Needs Optimization";
            }

            if (info.ActiveStartupAppsCount > 0 && info.ActiveAppsDelaySeconds > 1.5)
            {
                info.Recommendation = LocalizationManager.CurrentLanguage == "ar"
                    ? $"تعطيل برامج التحديث والخدمات غير الضرورية يوفر ~{info.ActiveAppsDelaySeconds:F1} ثانية من إقلاع جهازك."
                    : $"Disabling non-essential background updaters can shave ~{info.ActiveAppsDelaySeconds:F1}s off your boot time.";
            }
            else
            {
                info.Recommendation = LocalizationManager.CurrentLanguage == "ar"
                    ? "قائمة برامج بدء التشغيل محسنة ومثالية لأقصى سرعة إقلاع."
                    : "Your startup list is cleanly optimized for maximum boot speed.";
            }

            return info;
        }

        public static bool IsRunAtStartupEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
                return key?.GetValue("Tempo") != null;
            }
            catch { return false; }
        }

        public static bool SetRunAtStartup(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (key == null) return false;

                if (enable)
                {
                    string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                    if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                    {
                        exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tempo.exe");
                    }
                    key.SetValue("Tempo", $"\"{exePath}\"");
                }
                else
                {
                    key.DeleteValue("Tempo", false);
                }
                return true;
            }
            catch { return false; }
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
