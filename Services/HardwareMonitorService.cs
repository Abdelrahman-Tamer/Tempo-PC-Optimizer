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
using Tempo.Models;

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

    public class RunningProcessItem
    {
        public string AppKey { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string MainProcessName { get; set; } = "";
        public string ProcessName => MainProcessName;
        public double RamMb { get; set; }
        public string RamFormatted { get; set; } = "";
        public double RamPercentOfTotal { get; set; }
        public int InstanceCount { get; set; } = 1;
        public List<string> AssociatedProcesses { get; set; } = new();
        public ImageSource? IconSource { get; set; }
        public int MainPid { get; set; }
        public string MainPath { get; set; } = "";

        // Tag string for End Task button (comma-separated process names)
        public string ProcessNamesTag => AssociatedProcesses.Count > 0
            ? string.Join(",", AssociatedProcesses)
            : MainProcessName;

        // Clean, elegant metadata subtitle
        public string SubtitleText
        {
            get
            {
                bool isAr = LocalizationManager.CurrentLanguage == "ar";
                string procText = AssociatedProcesses.Count > 1
                    ? string.Join(", ", AssociatedProcesses)
                    : MainProcessName;

                if (InstanceCount > 1)
                {
                    return isAr
                        ? $"{InstanceCount} عمليات ({procText})"
                        : $"{InstanceCount} processes ({procText})";
                }
                return procText;
            }
        }

        public Visibility EndTaskVisibility => Visibility.Visible;
        public Visibility HasIconVisibility => IconSource != null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility FallbackIconVisibility => IconSource == null ? Visibility.Visible : Visibility.Collapsed;
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

        public bool IsSystemManaged { get; set; } = false;

        public Brush StatusDotBrush => !IsEnabled
            ? new SolidColorBrush(Color.FromRgb(100, 116, 139)) // Slate Muted
            : (IsSystemManaged
                ? new SolidColorBrush(Color.FromRgb(59, 130, 246)) // Royal Blue for System Protected
                : new SolidColorBrush(Color.FromRgb(16, 185, 129))); // TealHealth

        public string StatusLabel => !IsEnabled
            ? (LocalizationManager.CurrentLanguage == "ar" ? "معطّل" : "Disabled")
            : (IsSystemManaged
                ? (LocalizationManager.CurrentLanguage == "ar" ? "نظام (محمي)" : "System (Protected)")
                : (LocalizationManager.CurrentLanguage == "ar" ? "مفعّل" : "Enabled"));

        public string ImpactLabel => !IsEnabled
            ? (LocalizationManager.CurrentLanguage == "ar" ? "معطّل (0s)" : "Disabled (0s)")
            : (IsSystemManaged
                ? (LocalizationManager.CurrentLanguage == "ar" ? "خدمة نظام (0s)" : "System Service (0s)")
                : Impact switch
                {
                    StartupImpactLevel.High => (LocalizationManager.CurrentLanguage == "ar" ? "أثر مرتفع (+2.1s)" : "High Impact (+2.1s)"),
                    StartupImpactLevel.Medium => (LocalizationManager.CurrentLanguage == "ar" ? "أثر متوسط (+0.9s)" : "Medium Impact (+0.9s)"),
                    _ => (LocalizationManager.CurrentLanguage == "ar" ? "أثر خفيف (+0.3s)" : "Low Impact (+0.3s)")
                });

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

        public string ActionText => IsSystemManaged
            ? (LocalizationManager.CurrentLanguage == "ar" ? "محمي" : "Protected")
            : (IsEnabled 
                ? (LocalizationManager.CurrentLanguage == "ar" ? "تعطيل" : "Disable") 
                : (LocalizationManager.CurrentLanguage == "ar" ? "تفعيل" : "Enable"));

        public string LocationFriendly => Location switch
        {
            "HKCU" => (LocalizationManager.CurrentLanguage == "ar" ? "المستخدم (HKCU)" : "User (HKCU)"),
            "HKLM-32" => (LocalizationManager.CurrentLanguage == "ar" ? "النظام (HKLM 32-bit)" : "System (HKLM 32-bit)"),
            _ => (LocalizationManager.CurrentLanguage == "ar" ? "النظام (HKLM 64-bit)" : "System (HKLM 64-bit)")
        };

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

        private static bool GlobalMemoryStatusEx(MEMORYSTATUSEX lpBuffer) => NativeMethods.GlobalMemoryStatusEx(lpBuffer);

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

        public static readonly HashSet<string> ProtectedSystemProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "system", "idle", "registry", "smss", "csrss", "wininit", "services", "lsass", "winlogon",
            "fontdrvhost", "dwm", "memory compression", "svchost", "msmpeng", "securityhealthservice",
            "securityhealthhost", "tempo", "taskhostw", "sihost", "ctfmon", "explorer",
            "shellexperiencehost", "startmenuexperiencehost", "searchhost", "textinputhost",
            "runtimebroker", "applicationframehost", "systemsettings", "shellhost", "backgroundtaskhost",
            "conhost", "dllhost", "wlanext", "smartscreen", "widgets", "widgetservice",
            "phoneexperiencehost", "crossdeviceservice", "gameinputredistservice", "esrv", "dsatray",
            "smmonitor", "nvdisplay.container", "nvwmi64", "applemobiledevicelauncher",
            "applemobiledeviceprocess", "acrobatnotificationclient", "useroobebroker", "audiodg",
            "spoolsv", "werfault", "werfaultsecure", "crashpad_handler", "openconsole",
            "securityhealthsystray", "wmpf_installer", "tiworker", "trustedinstaller"
        };

        private static (string appKey, string displayName)? IdentifyApplication(string pName, string? path, IntPtr hwnd, string? title)
        {
            string pLower = pName.ToLowerInvariant();
            string pathLower = (path ?? "").ToLowerInvariant();

            // 1. Specific High-Profile Suites and Apps (Roll up helpers into root app)
            if (pathLower.Contains("antigravity ide"))
                return ("Antigravity IDE", "Antigravity IDE");

            if (pathLower.Contains(@"\programs\antigravity\") || pLower == "antigravity")
                return ("Antigravity", "Antigravity");

            if (pLower == "chrome" || pathLower.Contains(@"\google\chrome\"))
                return ("Google Chrome", "Google Chrome");

            if (pLower == "msedge" || pLower == "msedgewebview2" || pathLower.Contains(@"\microsoft\edge"))
                return ("Microsoft Edge", "Microsoft Edge");

            if (pLower.Contains("node") || pathLower.Contains(@"\nodejs\"))
                return ("Node.js", "Node.js JavaScript Runtime");

            if (pLower == "mspcmanager" || pLower == "mspcmanagercore")
                return ("MSPCManager", "Microsoft PC Manager");

            if (pLower == "open design" || pathLower.Contains(@"\open design\"))
                return ("Open Design", "Open Design");

            if (pLower == "spotify" || pLower == "spotifylauncher")
                return ("Spotify", "Spotify");

            if (pLower == "discord" || pathLower.Contains(@"\discord\"))
                return ("Discord", "Discord");

            if (pLower == "slack" || pathLower.Contains(@"\slack\"))
                return ("Slack", "Slack");

            if (pLower == "steam" || pathLower.Contains(@"\steam\"))
                return ("Steam", "Steam");

            if (pLower == "telegram" || pathLower.Contains(@"\telegram"))
                return ("Telegram", "Telegram Desktop");

            if (pLower == "code" || pathLower.Contains(@"\microsoft vs code\"))
                return ("VS Code", "Visual Studio Code");

            if (pLower == "devenv")
                return ("Visual Studio", "Microsoft Visual Studio");

            if (pLower == "powershell" || pLower == "pwsh")
                return ("PowerShell", "Windows PowerShell");

            if (pLower == "windowsterminal")
                return ("Windows Terminal", "Windows Terminal");

            if (pLower == "taskmgr")
                return ("Task Manager", "Task Manager");

            // 2. Any process with an active top-level user window
            if (hwnd != IntPtr.Zero && !string.IsNullOrWhiteSpace(title) && title.Length > 2)
            {
                string appName = pName;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    try
                    {
                        var vi = FileVersionInfo.GetVersionInfo(path);
                        if (!string.IsNullOrWhiteSpace(vi.FileDescription))
                            appName = vi.FileDescription;
                    }
                    catch { }
                }
                return (pName, appName);
            }

            // 3. User app installed in Program Files or AppData\Local\Programs with main exe
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                bool isUserAppDir = pathLower.Contains(@"\program files\") || 
                                   pathLower.Contains(@"\program files (x86)\") || 
                                   pathLower.Contains(@"\appdata\local\programs\");

                bool isSubHelper = pathLower.Contains(@"\resources\") || 
                                   pathLower.Contains(@"\bin\") || 
                                   pathLower.Contains(@"\helpers\") ||
                                   pathLower.Contains(@"\extensions\");

                if (isUserAppDir && !isSubHelper)
                {
                    string appName = pName;
                    try
                    {
                        var vi = FileVersionInfo.GetVersionInfo(path);
                        if (!string.IsNullOrWhiteSpace(vi.FileDescription))
                            appName = vi.FileDescription;
                    }
                    catch { }
                    return (pName, appName);
                }
            }

            // Non-primary background daemon / service -> skip
            return null;
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (ImageSource? icon, string displayName)> ProcessMetaCache = new(StringComparer.OrdinalIgnoreCase);

        private static string? GetProcessExePath(Process p)
        {
            try
            {
                return p.MainModule?.FileName;
            }
            catch
            {
                return null;
            }
        }

        private static (ImageSource? icon, string displayName) GetCachedProcessMetadata(string processName, string? exePath)
        {
            if (ProcessMetaCache.TryGetValue(processName, out var cached))
            {
                return cached;
            }

            ImageSource? icon = null;
            string displayName = processName;

            try
            {
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                {
                    icon = ExtractIconFromCommand(exePath);
                    var vi = FileVersionInfo.GetVersionInfo(exePath);
                    if (!string.IsNullOrWhiteSpace(vi.FileDescription))
                    {
                        displayName = vi.FileDescription;
                    }
                }
            }
            catch { }

            var meta = (icon, displayName);
            ProcessMetaCache[processName] = meta;
            return meta;
        }

        public List<RunningProcessItem> GetAllRunningProcesses(double totalRamMb = 0)
        {
            if (totalRamMb <= 0)
            {
                var (_, total, _, _) = GetRamMetrics();
                totalRamMb = total * 1024.0;
            }

            int currentSessionId = 1;
            try
            {
                using var currentProc = Process.GetCurrentProcess();
                currentSessionId = currentProc.SessionId;
            }
            catch { }

            Process[] processes = Array.Empty<Process>();
            try
            {
                processes = Process.GetProcesses();

                // Group by AppKey to aggregate all helper processes into the primary application
                var appMap = new Dictionary<string, (string appKey, string displayName, string mainProc, double totalRam, int instances, HashSet<string> procs, string? mainPath, int mainPid)>(StringComparer.OrdinalIgnoreCase);

                foreach (var p in processes)
                {
                    try
                    {
                        if (p.SessionId != currentSessionId)
                            continue;

                        string name = p.ProcessName;
                        if (string.IsNullOrWhiteSpace(name) || ProtectedSystemProcesses.Contains(name))
                            continue;

                        double wsMb = p.WorkingSet64 / (1024.0 * 1024.0);
                        if (wsMb < 1.0)
                            continue;

                        string? exePath = GetProcessExePath(p);
                        var match = IdentifyApplication(name, exePath, p.MainWindowHandle, p.MainWindowTitle);
                        if (match == null)
                            continue;

                        string appKey = match.Value.appKey;
                        string dispName = match.Value.displayName;

                        if (!appMap.TryGetValue(appKey, out var appData))
                        {
                            appData = (appKey, dispName, name, 0.0, 0, new HashSet<string>(StringComparer.OrdinalIgnoreCase), exePath, p.Id);
                        }

                        appData.totalRam += wsMb;
                        appData.instances += 1;
                        appData.procs.Add(name);
                        if (string.IsNullOrEmpty(appData.mainPath) && !string.IsNullOrEmpty(exePath))
                        {
                            appData.mainPath = exePath;
                        }

                        appMap[appKey] = appData;
                    }
                    catch { }
                }

                var result = new List<RunningProcessItem>();
                foreach (var kvp in appMap.Values)
                {
                    // Focus on primary applications consuming at least 10 MB
                    if (kvp.totalRam < 10.0)
                        continue;

                    double ramMb = Math.Round(kvp.totalRam, 1);
                    double ramPct = totalRamMb > 0 ? Math.Round((ramMb / totalRamMb) * 100.0, 1) : 0;
                    string ramFormatted = ramMb >= 1024.0
                        ? $"{(ramMb / 1024.0):F2} GB"
                        : $"{ramMb:F1} MB";

                    var (icon, _) = GetCachedProcessMetadata(kvp.mainProc, kvp.mainPath);

                    result.Add(new RunningProcessItem
                    {
                        AppKey = kvp.appKey,
                        DisplayName = kvp.displayName,
                        MainProcessName = kvp.mainProc,
                        RamMb = ramMb,
                        RamFormatted = ramFormatted,
                        RamPercentOfTotal = ramPct,
                        InstanceCount = kvp.instances,
                        AssociatedProcesses = kvp.procs.ToList(),
                        IconSource = icon,
                        MainPid = kvp.mainPid,
                        MainPath = kvp.mainPath ?? ""
                    });
                }

                return result.OrderByDescending(x => x.RamMb).ToList();
            }
            catch
            {
                return new List<RunningProcessItem>();
            }
            finally
            {
                foreach (var p in processes)
                {
                    try { p.Dispose(); } catch { }
                }
            }
        }

        public (bool success, double freedMb, string message) TerminateProcessGroup(string processNamesCommaSeparated)
        {
            if (string.IsNullOrWhiteSpace(processNamesCommaSeparated))
                return (false, 0, LocalizationManager.CurrentLanguage == "ar" ? "اسم العملية غير صالح." : "Invalid process name.");

            var names = processNamesCommaSeparated.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                                  .Select(n => n.Trim())
                                                  .Where(n => !string.IsNullOrEmpty(n))
                                                  .ToList();

            double freedMb = 0;
            int killedCount = 0;

            foreach (var processName in names)
            {
                if (ProtectedSystemProcesses.Contains(processName))
                    continue;

                Process[] procs = Array.Empty<Process>();
                try
                {
                    procs = Process.GetProcessesByName(processName);
                    foreach (var p in procs)
                    {
                        try
                        {
                            freedMb += p.WorkingSet64 / (1024.0 * 1024.0);
                            p.Kill();
                            killedCount++;
                        }
                        catch { }
                    }
                }
                catch { }
                finally
                {
                    foreach (var p in procs)
                    {
                        try { p.Dispose(); } catch { }
                    }
                }
            }

            if (killedCount > 0)
            {
                string msg = LocalizationManager.CurrentLanguage == "ar"
                    ? $"تم إغلاق التطبيق بنجاح وتوفير {freedMb:F1} ميجابايت."
                    : $"Successfully closed application and freed {freedMb:F1} MB.";
                return (true, Math.Round(freedMb, 1), msg);
            }
            else
            {
                string msg = LocalizationManager.CurrentLanguage == "ar"
                    ? "العملية لم تعد تعمل أو الوصول مرفوض."
                    : "Process is no longer running or access denied.";
                return (false, 0, msg);
            }
        }


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

        private static (bool isEnabled, bool isSystemManaged) IsAppEnabledInWindows(string appName, bool isUserScope)
        {
            try
            {
                // 1. Check HKCU (Run, Run32, StartupFolder)
                using (var baseCu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default))
                {
                    if (CheckApprovedKey(baseCu, @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", appName, out bool enCuRun, out bool sysCuRun))
                        return (enCuRun, sysCuRun);
                    if (CheckApprovedKey(baseCu, @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32", appName, out bool enCuRun32, out bool sysCuRun32))
                        return (enCuRun32, sysCuRun32);
                    if (CheckApprovedKey(baseCu, @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder", appName, out bool enCuFolder, out bool sysCuFolder))
                        return (enCuFolder, sysCuFolder);
                }

                // 2. Check HKLM in Registry64 view (Windows stores BOTH 64-bit and 32-bit StartupApproved here!)
                using (var baseLm64 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                {
                    if (CheckApprovedKey(baseLm64, @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", appName, out bool enLmRun, out bool sysLmRun))
                        return (enLmRun, sysLmRun);
                    if (CheckApprovedKey(baseLm64, @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32", appName, out bool enLmRun32, out bool sysLmRun32))
                        return (enLmRun32, sysLmRun32);
                    if (CheckApprovedKey(baseLm64, @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder", appName, out bool enLmFolder, out bool sysLmFolder))
                        return (enLmFolder, sysLmFolder);
                }
            }
            catch { }

            // Default in Windows: if no StartupApproved entry exists, it is enabled by default
            return (true, false);
        }

        private static bool CheckApprovedKey(RegistryKey baseKey, string subPath, string appName, out bool isEnabled, out bool isSystemManaged)
        {
            isEnabled = true;
            isSystemManaged = false;
            try
            {
                using var key = baseKey.OpenSubKey(subPath);
                if (key != null)
                {
                    var val = key.GetValue(appName);
                    if (val is byte[] bytes && bytes.Length > 0)
                    {
                        // In Windows StartupApproved binary blobs:
                        // Even byte 0 (0x02, 0x06, etc. where (byte[0] & 1) == 0) indicates ENABLED / Active.
                        // Odd byte 0 (0x03, 0x01, etc. where (byte[0] & 1) != 0) indicates DISABLED.
                        // Specifically, 0x06 indicates a system-managed / mandatory Windows service (like Windows Security).
                        isEnabled = (bytes[0] % 2 == 0);
                        isSystemManaged = (bytes[0] == 0x06);
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        public List<StartupAppItem> GetStartupApps()
        {
            var apps = new List<StartupAppItem>();
            const string runPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

            void ReadKey(RegistryHive hive, RegistryView view, string loc, bool isUserScope)
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

                        // 100% Authoritative Windows Startup Status Check
                        var (isEnabledInWindows, isSystemManaged) = IsAppEnabledInWindows(valueName, isUserScope);
                        bool isEnabled = pathExists && isEnabledInWindows;

                        var icon = ExtractIconFromCommand(cmd);
                        var baseImpact = ClassifyImpact(valueName, cmd);

                        apps.Add(new StartupAppItem
                        {
                            Name = valueName,
                            Command = cmd,
                            Location = loc,
                            IsEnabled = isEnabled,
                            IsSystemManaged = isSystemManaged,
                            Impact = isEnabled ? baseImpact : StartupImpactLevel.Disabled,
                            IconSource = icon
                        });
                    }
                }
                catch { }
            }

            ReadKey(RegistryHive.CurrentUser, RegistryView.Default, "HKCU", true);
            ReadKey(RegistryHive.LocalMachine, RegistryView.Registry64, "HKLM-64", false);
            ReadKey(RegistryHive.LocalMachine, RegistryView.Registry32, "HKLM-32", false);

            return apps;
        }

        public bool ToggleStartupApp(StartupAppItem app)
        {
            try
            {
                byte targetByte = app.IsEnabled ? (byte)0x03 : (byte)0x02; // 0x03 = Disabled, 0x02 = Enabled
                byte[] newBytes = new byte[] { targetByte, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

                if (app.IsUserScope)
                {
                    const string approvedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
                    using var approvedKey = Registry.CurrentUser.CreateSubKey(approvedPath, true);
                    if (approvedKey != null)
                    {
                        approvedKey.SetValue(app.Name, newBytes, RegistryValueKind.Binary);
                        app.IsEnabled = !app.IsEnabled;
                        app.Impact = app.IsEnabled ? ClassifyImpact(app.Name, app.Command) : StartupImpactLevel.Disabled;
                        return true;
                    }
                }
                else
                {
                    // HKLM Scope (System-wide): check if 32-bit (Run32) or 64-bit (Run)
                    string subPath = app.Location.Contains("32", StringComparison.OrdinalIgnoreCase)
                        ? @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32"
                        : @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

                    // Try direct registry write first
                    try
                    {
                        using var hklm64 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                        using var key = hklm64.CreateSubKey(subPath, true);
                        if (key != null)
                        {
                            key.SetValue(app.Name, newBytes, RegistryValueKind.Binary);
                            app.IsEnabled = !app.IsEnabled;
                            app.Impact = app.IsEnabled ? ClassifyImpact(app.Name, app.Command) : StartupImpactLevel.Disabled;
                            return true;
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        if (WriteHklmStartupApprovedElevated(subPath, app.Name, newBytes))
                        {
                            app.IsEnabled = !app.IsEnabled;
                            app.Impact = app.IsEnabled ? ClassifyImpact(app.Name, app.Command) : StartupImpactLevel.Disabled;
                            return true;
                        }
                    }
                    catch (System.Security.SecurityException)
                    {
                        if (WriteHklmStartupApprovedElevated(subPath, app.Name, newBytes))
                        {
                            app.IsEnabled = !app.IsEnabled;
                            app.Impact = app.IsEnabled ? ClassifyImpact(app.Name, app.Command) : StartupImpactLevel.Disabled;
                            return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        private static bool WriteHklmStartupApprovedElevated(string subPath, string appName, byte[] bytes, bool elevate = true)
        {
            try
            {
                // Strict validation: reject any null, empty, or control characters in appName
                if (string.IsNullOrWhiteSpace(appName) || appName.Any(char.IsControl))
                    return false;

                // Transfer all arguments as Base64 strings to prevent any shell/script command injection
                string b64SubPath = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(subPath));
                string b64AppName = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(appName));
                string b64Bytes = Convert.ToBase64String(bytes);

                string script =
                    "$ErrorActionPreference = 'Stop'; " +
                    "try { " +
                    $"$subPath = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String('{b64SubPath}')); " +
                    $"$name = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String('{b64AppName}')); " +
                    $"$data = [System.Convert]::FromBase64String('{b64Bytes}'); " +
                    $"$k = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64).CreateSubKey($subPath); " +
                    $"$k.SetValue($name, $data, [Microsoft.Win32.RegistryValueKind]::Binary); " +
                    $"$k.Dispose(); " +
                    "exit 0; " +
                    "} catch { exit 1; }";

                // Encode the entire script as UTF-16LE (Unicode) Base64 for powershell -EncodedCommand
                byte[] unicodeBytes = System.Text.Encoding.Unicode.GetBytes(script);
                string encodedCommand = Convert.ToBase64String(unicodeBytes);

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand {encodedCommand}",
                    Verb = elevate ? "runas" : "",
                    UseShellExecute = elevate,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    proc.WaitForExit(8000);
                    return proc.ExitCode == 0;
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
