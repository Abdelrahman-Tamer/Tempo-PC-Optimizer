using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
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
        public string MediaType { get; set; } = "Unknown"; // SSD, HDD, or Unknown
        public bool IsSsd => MediaType.Equals("SSD", StringComparison.OrdinalIgnoreCase);
        public bool IsUnknown => MediaType.Equals("Unknown", StringComparison.OrdinalIgnoreCase);
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
        public double? BiosTimeSeconds { get; set; }
        public DateTime? LastBootUpTime { get; set; }
        public TimeSpan? Uptime { get; set; }
        public double? MainPathBootSeconds { get; set; }
        public double? ActiveAppsDelaySeconds { get; set; }
        public double? EstimatedTotalBootSeconds => MainPathBootSeconds;
        public int ActiveStartupAppsCount { get; set; }
        public int DisabledStartupAppsCount { get; set; }
        public bool IsMeasured { get; set; }
        public bool IsCacheStale { get; set; }
        public string Rating { get; set; } = "";
        public string RatingText { get; set; } = "";
        public string Recommendation { get; set; } = "";
    }

    public class BootMeasureCache
    {
        public DateTime Timestamp { get; set; }
        public string BootId { get; set; } = "";
        public long MainPathBootMs { get; set; }
        public List<BootMeasureAppItem> Apps { get; set; } = new();
    }

    public class BootMeasureAppItem
    {
        public string ProcessName { get; set; } = "";
        public long CpuMs { get; set; }
        public long DiskBytes { get; set; }
    }

    public enum BootMeasureResult
    {
        Success,
        UserCancelledUac,
        Failed
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
        public bool HasActiveWindow { get; set; } = false;
        public string Monogram => !string.IsNullOrEmpty(DisplayName)
            ? DisplayName.Substring(0, 1).ToUpperInvariant()
            : (!string.IsNullOrEmpty(MainProcessName) ? MainProcessName.Substring(0, 1).ToUpperInvariant() : "A");

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
                string stateText = HasActiveWindow
                    ? (isAr ? "نافذة نشطة" : "Active Window")
                    : (isAr ? "يعمل في الخلفية" : "Background App");

                if (InstanceCount > 1)
                {
                    return isAr
                        ? $"{stateText} • {InstanceCount} عمليات"
                        : $"{stateText} • {InstanceCount} processes";
                }
                return stateText;
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
        public Visibility HasIconVisibility => IconSource != null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility FallbackIconVisibility => IconSource == null ? Visibility.Visible : Visibility.Collapsed;
        public string Monogram => !string.IsNullOrEmpty(Name) ? Name.Substring(0, 1).ToUpperInvariant() : "A";
        public bool IsUserScope => Location.Contains("HKCU", StringComparison.OrdinalIgnoreCase);

        public bool IsSystemManaged { get; set; } = false;
        public long? MeasuredCpuMs { get; set; }
        public long? MeasuredDiskBytes { get; set; }
        public bool HasRealMeasurement { get; set; }

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

        public string ImpactLabel
        {
            get
            {
                if (!IsEnabled)
                    return LocalizationManager.GetString("StartupImpactDisabled", LocalizationManager.CurrentLanguage == "ar" ? "معطّل" : "Disabled");
                if (IsSystemManaged)
                    return LocalizationManager.GetString("StartupImpactSystem", LocalizationManager.CurrentLanguage == "ar" ? "خدمة نظام" : "System Service");

                if (HasRealMeasurement)
                {
                    if (!MeasuredCpuMs.HasValue && !MeasuredDiskBytes.HasValue)
                    {
                        return LocalizationManager.GetString("BootNotMeasured", LocalizationManager.CurrentLanguage == "ar" ? "لم يُقاس" : "Not measured");
                    }
                    return Impact switch
                    {
                        StartupImpactLevel.High => LocalizationManager.CurrentLanguage == "ar" ? "أثر مرتفع" : "High Impact",
                        StartupImpactLevel.Medium => LocalizationManager.CurrentLanguage == "ar" ? "أثر متوسط" : "Medium Impact",
                        _ => LocalizationManager.CurrentLanguage == "ar" ? "أثر خفيف" : "Low Impact"
                    };
                }

                return Impact switch
                {
                    StartupImpactLevel.High => LocalizationManager.GetString("StartupImpactHigh", LocalizationManager.CurrentLanguage == "ar" ? "أثر مرتفع (تقديري)" : "High Impact (est.)"),
                    StartupImpactLevel.Medium => LocalizationManager.GetString("StartupImpactMedium", LocalizationManager.CurrentLanguage == "ar" ? "أثر متوسط (تقديري)" : "Medium Impact (est.)"),
                    _ => LocalizationManager.GetString("StartupImpactLow", LocalizationManager.CurrentLanguage == "ar" ? "أثر خفيف (تقديري)" : "Low Impact (est.)")
                };
            }
        }

        public Brush ImpactBadgeBg => (!IsEnabled || (HasRealMeasurement && !MeasuredCpuMs.HasValue && !MeasuredDiskBytes.HasValue))
            ? new SolidColorBrush(Color.FromArgb(25, 100, 116, 139))
            : Impact switch
            {
                StartupImpactLevel.High => new SolidColorBrush(Color.FromArgb(35, 244, 63, 94)),
                StartupImpactLevel.Medium => new SolidColorBrush(Color.FromArgb(35, 245, 158, 11)),
                _ => new SolidColorBrush(Color.FromArgb(30, 16, 185, 129))
            };

        public Brush ImpactBadgeFg => (!IsEnabled || (HasRealMeasurement && !MeasuredCpuMs.HasValue && !MeasuredDiskBytes.HasValue))
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
    }

    public class HardwareMonitorService : IDisposable
    {
        private readonly Computer _computer;
        private PerformanceCounter? _cpuCounter;
        private long _lastNetRecvBytes = -1;
        private long _lastNetSentBytes = -1;
        private DateTime _lastNetTime = DateTime.MinValue;
        private readonly object _netLock = new();
        private NetworkInterface[]? _cachedNics = null; // Cached network interfaces (Fix 4)

        private static readonly object _logLock = new();
        private const long MaxLogSizeBytes = 5 * 1024 * 1024; // 5 MB rotation threshold
        private static readonly string _logFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Tempo",
            "hardware.log");

        public static void Log(string message, string level = "INFO")
        {
            try
            {
                lock (_logLock)
                {
                    string dir = Path.GetDirectoryName(_logFilePath)!;
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                    var fi = new FileInfo(_logFilePath);
                    if (fi.Exists && fi.Length > MaxLogSizeBytes)
                    {
                        string oldLog = _logFilePath + ".old";
                        File.Move(_logFilePath, oldLog, overwrite: true);
                    }

                    string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";
                    File.AppendAllText(_logFilePath, line + Environment.NewLine);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
            }
        }

        private static bool GlobalMemoryStatusEx(MEMORYSTATUSEX lpBuffer) => NativeMethods.GlobalMemoryStatusEx(lpBuffer);

        private volatile bool _isComputerOpen = false;
        private string? _cachedGpuName = null; // Cached after first successful WMI GPU query (Fix 3)

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
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    Log($"CPU PerformanceCounter notice: {ex.Message}", "DEBUG");
                }

                try
                {
                    _computer.Open();
                    _isComputerOpen = true;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    Log($"LibreHardwareMonitor Open notice: {ex.Message}", "DEBUG");
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
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Log($"GetBaseCpuClockMhz failed: {ex.Message}", "DEBUG");
            }

            _detectedCpuBaseClockMhz = 0f;
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
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        Log($"GetCpuMetrics counter sample failed: {ex.Message}", "DEBUG");
                    }
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
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Log($"GetCpuMetrics hardware sensor traversal failed: {ex.Message}", "DEBUG");
            }

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
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    Log($"GetGpuMetrics sensor update failed: {ex.Message}", "DEBUG");
                }
            }

            // WMI fallback: only query once and cache the result (Fix 3)
            if (_cachedGpuName != null)
            {
                return (_cachedGpuName, null, null, 0, 0);
            }

            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
                using var coll = searcher.Get();
                foreach (ManagementObject mo in coll)
                {
                    using (mo)
                    {
                        string? name = mo["Name"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            _cachedGpuName = name;
                            return (name, null, null, 0, 0);
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Log($"GetGpuMetrics WMI query failed: {ex.Message}", "DEBUG");
            }

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
                bool needRefresh = false;

                // Use cached NICs to avoid the heavy OS call every 1.5s (Fix 4)
                if (_cachedNics == null)
                {
                    _cachedNics = NetworkInterface.GetAllNetworkInterfaces();
                }

                foreach (var nic in _cachedNics)
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
                        catch (Exception ex) when (ex is not OutOfMemoryException)
                        {
                            // Adapter may have gone away — force a refresh next tick
                            needRefresh = true;
                            Log($"Network interface stats query failed: {ex.Message}", "DEBUG");
                        }
                    }
                }

                if (needRefresh)
                {
                    _cachedNics = null;
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
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _cachedNics = null; // Force refresh on next call after unexpected failure
                Log($"GetNetworkMetrics failed: {ex.Message}", "DEBUG");
                return (0, 0, "0.0 KB/s", "0.0 KB/s");
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;      // 0 = Offline, 1 = Online, 255 = Unknown
            public byte BatteryFlag;       // 1 = High, 2 = Low, 4 = Critical, 8 = Charging, 128 = No battery, 255 = Unknown
            public byte BatteryLifePercent;// 0..100, 255 = Unknown
            public byte SystemStatusFlag;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }

        private static string? _cachedCpuModelName = null;
        public string GetCpuName()
        {
            if (_cachedCpuModelName != null) return _cachedCpuModelName;
            try
            {
                if (_isComputerOpen)
                {
                    foreach (var hw in _computer.Hardware)
                    {
                        if (hw.HardwareType == HardwareType.Cpu && !string.IsNullOrWhiteSpace(hw.Name))
                        {
                            _cachedCpuModelName = CleanCpuName(hw.Name);
                            return _cachedCpuModelName;
                        }
                    }
                }

                // Fallback: Registry
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                string? regName = key?.GetValue("ProcessorNameString") as string;
                if (!string.IsNullOrWhiteSpace(regName))
                {
                    _cachedCpuModelName = CleanCpuName(regName);
                    return _cachedCpuModelName;
                }
            }
            catch { }

            _cachedCpuModelName = "Intel / AMD Processor";
            return _cachedCpuModelName;
        }

        private static string CleanCpuName(string raw)
        {
            return raw.Replace("(R)", "")
                      .Replace("(TM)", "")
                      .Replace("CPU", "")
                      .Replace("@", "")
                      .Trim();
        }

        private static string? _cachedDeviceModel = null;
        public string GetDeviceModel()
        {
            if (_cachedDeviceModel != null) return _cachedDeviceModel;
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Model FROM Win32_ComputerSystem");
                foreach (ManagementObject mo in searcher.Get())
                {
                    using (mo)
                    {
                        string mfg = (mo["Manufacturer"] as string ?? "").Trim();
                        string model = (mo["Model"] as string ?? "").Trim();
                        if (mfg.Contains("System manufacturer", StringComparison.OrdinalIgnoreCase)) mfg = "";
                        if (model.Contains("System Product Name", StringComparison.OrdinalIgnoreCase)) model = "";

                        string combined = string.IsNullOrEmpty(mfg) ? model : $"{mfg} {model}".Trim();
                        if (!string.IsNullOrWhiteSpace(combined))
                        {
                            _cachedDeviceModel = combined.Replace("Dell Inc.", "Dell").Trim();
                            return _cachedDeviceModel;
                        }
                    }
                }
            }
            catch { }

            _cachedDeviceModel = "Windows PC";
            return _cachedDeviceModel;
        }

        private static string? _cachedRamSpecs = null;
        public string GetRamHardwareSpecs()
        {
            if (_cachedRamSpecs != null) return _cachedRamSpecs;
            try
            {
                long totalBytes = 0;
                uint speed = 0;
                uint smbiosType = 0;
                int modulesCount = 0;

                using var searcher = new ManagementObjectSearcher("SELECT Capacity, Speed, SMBIOSMemoryType FROM Win32_PhysicalMemory");
                foreach (ManagementObject mo in searcher.Get())
                {
                    using (mo)
                    {
                        if (mo["Capacity"] != null && long.TryParse(mo["Capacity"].ToString(), out long cap))
                        {
                            totalBytes += cap;
                            modulesCount++;
                        }
                        if (speed == 0 && mo["Speed"] != null && uint.TryParse(mo["Speed"].ToString(), out uint sp))
                        {
                            speed = sp;
                        }
                        if (smbiosType == 0 && mo["SMBIOSMemoryType"] != null && uint.TryParse(mo["SMBIOSMemoryType"].ToString(), out uint st))
                        {
                            smbiosType = st;
                        }
                    }
                }

                if (totalBytes > 0)
                {
                    double totalGb = Math.Round(totalBytes / (1024.0 * 1024.0 * 1024.0), 0);
                    string typeStr = smbiosType switch
                    {
                        26 => "DDR4",
                        34 => "DDR5",
                        24 => "DDR3",
                        _ => "RAM"
                    };

                    string speedStr = speed > 0 ? $" @ {speed} MHz" : "";
                    string slotsStr = modulesCount > 1 ? $" • {modulesCount} Slots" : "";
                    _cachedRamSpecs = $"{totalGb:F0} GB {typeStr}{speedStr}{slotsStr}";
                    return _cachedRamSpecs;
                }
            }
            catch { }

            var (tot, _, _, _) = GetRamMetrics();
            _cachedRamSpecs = $"{tot:F0} GB RAM";
            return _cachedRamSpecs;
        }

        public string GetSystemUptimeFormatted()
        {
            var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
            bool isAr = LocalizationManager.CurrentLanguage == "ar";

            if (uptime.TotalDays >= 1)
            {
                int days = (int)uptime.TotalDays;
                int hours = uptime.Hours;
                return isAr
                    ? (days == 1 ? $"يوم و {hours} س" : (days == 2 ? $"يومان و {hours} س" : $"{days} أيام و {hours} س"))
                    : $"{days}d {hours}h {uptime.Minutes}m";
            }
            if (uptime.TotalHours >= 1)
            {
                return isAr
                    ? $"{uptime.Hours} س و {uptime.Minutes} د"
                    : $"{uptime.Hours}h {uptime.Minutes}m";
            }
            return isAr
                ? $"{uptime.Minutes} دقيقة"
                : $"{uptime.Minutes}m {uptime.Seconds}s";
        }

        public (bool hasBattery, string powerText) GetPowerAndBatteryStatus()
        {
            if (GetSystemPowerStatus(out var status))
            {
                bool isAr = LocalizationManager.CurrentLanguage == "ar";
                bool noBattery = status.BatteryFlag == 128 || status.BatteryLifePercent == 255;
                bool isPluggedIn = status.ACLineStatus == 1;
                bool isCharging = (status.BatteryFlag & 8) != 0;

                if (noBattery)
                {
                    return (false, isAr ? "جهاز مكتبي (متصل بالكهرباء)" : "Desktop (AC Power)");
                }

                int pct = Math.Min(100, (int)status.BatteryLifePercent);
                string stateStr;
                if (isCharging)
                {
                    stateStr = isAr ? "جارٍ الشحن ⚡" : "Charging ⚡";
                }
                else if (isPluggedIn)
                {
                    stateStr = isAr ? "متصل بالشاحن" : "Plugged In";
                }
                else
                {
                    stateStr = isAr ? "على البطارية" : "On Battery";
                }

                return (true, $"{pct}% ({stateStr})");
            }

            return (false, "AC Power");
        }

        private static long _lastPingMs = -1;
        private static DateTime _lastPingTime = DateTime.MinValue;
        private static bool _isPinging = false;

        public long GetNetworkPingMs()
        {
            if ((DateTime.UtcNow - _lastPingTime).TotalSeconds >= 4 && !_isPinging)
            {
                _isPinging = true;
                Task.Run(async () =>
                {
                    try
                    {
                        using var pinger = new System.Net.NetworkInformation.Ping();
                        var reply = await pinger.SendPingAsync("1.1.1.1", 300);
                        if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                        {
                            _lastPingMs = reply.RoundtripTime;
                        }
                        else
                        {
                            _lastPingMs = -1;
                        }
                    }
                    catch
                    {
                        _lastPingMs = -1;
                    }
                    finally
                    {
                        _lastPingTime = DateTime.UtcNow;
                        _isPinging = false;
                    }
                });
            }

            return _lastPingMs;
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
                        using var diskColl = diskSearcher.Get();
                        foreach (ManagementObject disk in diskColl)
                        {
                            using (disk)
                            {
                                string devId = disk["DeviceId"]?.ToString() ?? "";
                                ushort mType = disk["MediaType"] != null ? Convert.ToUInt16(disk["MediaType"]) : (ushort)0;
                                string typeStr = (mType == 4 || mType == 5) ? "SSD" : (mType == 3 ? "HDD" : "Unknown");
                                diskTypes[devId] = typeStr;
                            }
                        }

                        using var partSearcher = new ManagementObjectSearcher(
                            @"root\Microsoft\Windows\Storage",
                            "SELECT DiskNumber, DriveLetter FROM MSFT_Partition WHERE DriveLetter IS NOT NULL");
                        using var partColl = partSearcher.Get();
                        foreach (ManagementObject part in partColl)
                        {
                            using (part)
                            {
                                string diskNum = part["DiskNumber"]?.ToString() ?? "";
                                string driveLetter = part["DriveLetter"]?.ToString() ?? "";
                                driveLetter = driveLetter.Trim().TrimEnd(':');
                                if (!string.IsNullOrEmpty(driveLetter) && char.IsLetter(driveLetter[0]) && diskTypes.TryGetValue(diskNum, out string? mediaType))
                                {
                                    _cachedDiskMediaMap[driveLetter.ToUpperInvariant()] = mediaType!;
                                }
                            }
                        }
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        Log($"Disk media mapping failed: {ex.Message}", "DEBUG");
                    }
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
                    string mediaType = "Unknown";
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
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    Log($"DriveInfo query skipped for drive: {ex.Message}", "DEBUG");
                }
            }

            return list;
        }

        // Fix 9: Named value struct to replace anonymous LINQ object (reduces GC pressure)
        private struct ProcessRamEntry
        {
            public string Name;
            public double WorkingSetMb;
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
                            return new ProcessRamEntry { Name = p.ProcessName, WorkingSetMb = p.WorkingSet64 / (1024.0 * 1024.0) };
                        }
                        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                        {
                            return new ProcessRamEntry { Name = "", WorkingSetMb = 0.0 };
                        }
                    })
                    .Where(p => !string.IsNullOrEmpty(p.Name))
                    .GroupBy(p => p.Name)
                    .Select(g => (processName: g.Key, ramMb: Math.Round(g.Sum(x => x.WorkingSetMb), 1)))
                    .OrderByDescending(x => x.ramMb)
                    .Take(5)
                    .ToArray();
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
                Log($"GetTop5RamProcesses query failed: {ex.Message}", "DEBUG");
                return Array.Empty<(string, double)>();
            }
            finally
            {
                foreach (var p in processes)
                {
                    try { p.Dispose(); } catch (Exception ex) when (ex is not OutOfMemoryException) { }
                }
            }
        }

        public static readonly HashSet<string> ProtectedSystemProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "system", "idle", "registry", "smss", "csrss", "wininit", "services", "lsass", "winlogon",
            "fontdrvhost", "dwm", "memory compression", "svchost", "msmpeng", "securityhealthservice",
            "securityhealthhost", "taskhostw", "sihost", "ctfmon", "explorer",
            "shellexperiencehost", "startmenuexperiencehost", "searchhost", "textinputhost",
            "runtimebroker", "applicationframehost", "systemsettings", "shellhost", "backgroundtaskhost",
            "conhost", "dllhost", "wlanext", "smartscreen", "spoolsv", "audiodg",
            "werfault", "werfaultsecure", "tiworker", "trustedinstaller", "useroobebroker",
            "tempo"
        };

        public static bool IsSystemOrServiceProcess(string pName, string? exePath, IntPtr hwnd, string? windowTitle)
        {
            if (string.IsNullOrWhiteSpace(pName)) return true;

            string lowerName = pName.ToLowerInvariant();
            if (ProtectedSystemProcesses.Contains(lowerName))
                return true;

            // Known system-only background services & daemons in user session
            if (lowerName is "wsqmcons" or "devicecensus" or "compattelrunner" or "mousocoreworker" or 
                "sedlauncher" or "usoclient" or "gamebarftpresencewriter" or "systemsettingsbroker" or 
                "sihost" or "ctfmon" or "taskhostw" or "dllhost" or "conhost" or "svchost" or "dashost" or
                "securityhealthservice" or "securityhealthhost" or "smartscreen" or "spoolsv" or "audiodg")
            {
                return true;
            }

            bool hasWindow = hwnd != IntPtr.Zero && !string.IsNullOrWhiteSpace(windowTitle) && windowTitle.Length > 0;

            if (!string.IsNullOrEmpty(exePath))
            {
                string pathLower = exePath.ToLowerInvariant();

                // Core Windows system directories
                bool inSystemDir = pathLower.Contains(@"\windows\system32\") ||
                                   pathLower.Contains(@"\windows\syswow64\") ||
                                   pathLower.Contains(@"\windows\systemapps\");

                if (inSystemDir)
                {
                    // Allowed interactive Windows built-in utilities
                    bool isAllowedUtility = lowerName is "notepad" or "calc" or "calculatorapp" or "taskmgr" or 
                                            "mspaint" or "regedit" or "snippingtool" or "powershell" or 
                                            "windowsterminal" or "wt" or "cmd" or "write";

                    // If it's located in System32 and has no user window and is not an allowed utility, it's a background OS service
                    if (!isAllowedUtility && !hasWindow)
                        return true;
                }
            }

            return false;
        }

        private static (string appKey, string displayName)? IdentifyApplication(string pName, string? path, IntPtr hwnd, string? title)
        {
            string pLower = pName.ToLowerInvariant();
            string pathLower = (path ?? "").ToLowerInvariant();

            // 1. Roll up known multi-process suites into a single clean application card:

            // Google Chrome
            if (pLower == "chrome" || pathLower.Contains(@"\google\chrome\"))
                return ("Google Chrome", "Google Chrome");

            // Brave Browser
            if (pLower == "brave" || pathLower.Contains(@"\bravesoftware\"))
                return ("Brave Browser", "Brave Browser");

            // Microsoft Edge
            if (pLower == "msedge" || pathLower.Contains(@"\microsoft\edge\application\msedge.exe"))
                return ("Microsoft Edge", "Microsoft Edge");

            // Microsoft Edge WebView2 (Helper embedded in various apps)
            if (pLower == "msedgewebview2" || pathLower.Contains(@"\edgewebview\"))
                return ("Microsoft Edge WebView2", "Microsoft Edge WebView2");

            // Google Gemini
            if (pLower.Contains("gemini") || pathLower.Contains(@"\google\gemini\"))
                return ("Google Gemini", "Google Gemini");

            // Telegram
            if (pLower.Contains("telegram") || pathLower.Contains(@"\telegram"))
                return ("Telegram", "Telegram Desktop");

            // WhatsApp
            if (pLower.Contains("whatsapp") || pathLower.Contains(@"\whatsapp"))
                return ("WhatsApp", "WhatsApp");

            // Claude
            if (pLower == "claude" || pathLower.Contains(@"\anthropicclaude\"))
                return ("Claude", "Claude");

            // Antigravity IDE & Antigravity
            if (pathLower.Contains("antigravity ide"))
                return ("Antigravity IDE", "Antigravity IDE");

            if (pLower == "antigravity-manager" || pLower == "antigravity manager")
                return ("Antigravity Manager", "Antigravity Manager");

            if (pLower == "antigravity" || pLower == "language_server" || pathLower.Contains(@"\programs\antigravity\"))
                return ("Antigravity", "Antigravity");

            // KDE Connect
            if (pLower.Contains("kdeconnect"))
                return ("KDE Connect", "KDE Connect");

            // Screen Recorders (iScreen, etc.)
            if (pLower.StartsWith("iscr") || pLower.Contains("screenrec"))
                return ("Screen Recorder", "iScreen Recorder");

            // OBS Studio
            if (pLower.StartsWith("obs") || pathLower.Contains(@"\obs-studio\"))
                return ("OBS Studio", "OBS Studio");

            // Node.js
            if (pLower.Contains("node") || pathLower.Contains(@"\nodejs\"))
                return ("Node.js", "Node.js JavaScript Runtime");

            // Docker
            if (pLower.Contains("docker") || pathLower.Contains(@"\docker"))
                return ("Docker", "Docker Desktop");

            // Microsoft PC Manager
            if (pLower == "mspcmanager" || pLower == "mspcmanagercore")
                return ("MSPCManager", "Microsoft PC Manager");

            // Open Design
            if (pLower == "open design" || pathLower.Contains(@"\open design\") || pLower.StartsWith("opendesign"))
                return ("Open Design", "Open Design");

            // Spotify
            if (pLower.Contains("spotify") || pathLower.Contains(@"\spotify\"))
                return ("Spotify", "Spotify");

            // Discord
            if (pLower.Contains("discord") || pathLower.Contains(@"\discord\"))
                return ("Discord", "Discord");

            // Slack
            if (pLower.Contains("slack") || pathLower.Contains(@"\slack\"))
                return ("Slack", "Slack");

            // Steam
            if (pLower.Contains("steam") || pathLower.Contains(@"\steam\"))
                return ("Steam", "Steam");

            // Epic Games Launcher
            if (pLower.Contains("epicgames") || pathLower.Contains(@"\epic games\"))
                return ("Epic Games", "Epic Games Launcher");

            // Sunshine Streaming
            if (pLower == "sunshine")
                return ("Sunshine", "Sunshine Game Stream");

            // VLC Media Player
            if (pLower == "vlc" || pathLower.Contains(@"\videolan\vlc\"))
                return ("VLC Media Player", "VLC Media Player");

            // Internet Download Manager (IDM)
            if (pLower == "idman" || pathLower.Contains(@"\internet download manager\"))
                return ("IDM", "Internet Download Manager");

            // qBittorrent & uTorrent
            if (pLower == "qbittorrent")
                return ("qBittorrent", "qBittorrent");
            if (pLower == "utorrent")
                return ("uTorrent", "µTorrent");

            // Zoom & Microsoft Teams
            if (pLower.Contains("zoom"))
                return ("Zoom", "Zoom Workplace");
            if (pLower == "teams" || pLower == "ms-teams")
                return ("Microsoft Teams", "Microsoft Teams");

            // GitHub Desktop & Git
            if (pLower.Contains("githubdesktop") || pathLower.Contains(@"\githubdesktop\"))
                return ("GitHub Desktop", "GitHub Desktop");

            // Postman
            if (pLower == "postman")
                return ("Postman", "Postman");

            // Notepad++
            if (pLower == "notepad++")
                return ("Notepad++", "Notepad++");

            // WinRAR & 7-Zip
            if (pLower == "winrar")
                return ("WinRAR", "WinRAR Archiver");
            if (pLower == "7zfm" || pLower == "7zg")
                return ("7-Zip", "7-Zip File Manager");

            // VS Code
            if (pLower == "code" || pathLower.Contains(@"\microsoft vs code\"))
                return ("VS Code", "Visual Studio Code");

            // Visual Studio
            if (pLower == "devenv")
                return ("Visual Studio", "Microsoft Visual Studio");

            // PowerShell
            if (pLower == "powershell" || pLower == "pwsh")
                return ("PowerShell", "Windows PowerShell");

            // Windows Terminal
            if (pLower == "windowsterminal" || pLower == "wt")
                return ("Windows Terminal", "Windows Terminal");

            // Task Manager
            if (pLower == "taskmgr")
                return ("Task Manager", "Task Manager");

            // Calculator
            if (pLower == "calc" || pLower == "calculatorapp")
                return ("Calculator", "Windows Calculator");

            // Notepad
            if (pLower == "notepad")
                return ("Notepad", "Windows Notepad");

            // KeePass
            if (pLower.Contains("keepass"))
                return ("KeePass", "KeePass Password Safe");

            // App Market
            if (pLower == "appmarket")
                return ("App Market", "App Market");

            // Intel Driver & Support Assistant
            if (pLower == "dsatray" || pLower.Contains("intel driver"))
                return ("Intel DSA", "Intel Driver & Support Assistant");

            // NVIDIA Settings
            if (pLower.Contains("nvdisplay") || pLower.Contains("nvwmi"))
                return ("NVIDIA Settings", "NVIDIA Settings");

            // 2. Filter out raw crashpad helpers to prevent unhelpful clutter
            if (pLower == "crashpad_handler" || pLower.EndsWith("crashpadhandler"))
                return null;

            // 3. For all other interactive and background applications running in the user session:
            // Extract a rich friendly display name from FileVersionInfo or ProductName if available
            string displayName = pName;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                try
                {
                    var vi = FileVersionInfo.GetVersionInfo(path);
                    if (!string.IsNullOrWhiteSpace(vi.FileDescription))
                    {
                        displayName = vi.FileDescription.Trim();
                    }
                    else if (!string.IsNullOrWhiteSpace(vi.ProductName))
                    {
                        displayName = vi.ProductName.Trim();
                    }
                }
                catch
                {
                    // Fall back to clean process name
                }
            }

            // If displayName is too short, empty, or generic fallback to pName
            if (string.IsNullOrWhiteSpace(displayName) || displayName.Length < 2)
            {
                displayName = pName;
            }

            return (pName, displayName);
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (ImageSource? icon, string displayName)> ProcessMetaCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string?> AppPathLookupCache = new(StringComparer.OrdinalIgnoreCase);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        // QueryFullProcessImageName works reliably with OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION) for user/elevated processes
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(
            IntPtr hProcess,
            int dwFlags,
            System.Text.StringBuilder lpExeName,
            ref int lpdwSize);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern uint PrivateExtractIcons(
            string lpszFile,
            int nIconIndex,
            int cxIcon,
            int cyIcon,
            IntPtr[] phicon,
            uint[] piconid,
            uint nIcons,
            uint flags);

        [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
        private static extern IntPtr GetClassLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetClassLongW")]
        private static extern IntPtr GetClassLong32(IntPtr hWnd, int nIndex);

        private static IntPtr GetClassLongPtr(IntPtr hWnd, int nIndex)
        {
            return IntPtr.Size == 8 ? GetClassLongPtr64(hWnd, nIndex) : GetClassLong32(hWnd, nIndex);
        }

        private const int GCLP_HICON = -14;
        private const int GCLP_HICONSM = -34;

        private static string? GetProcessExePath(Process p)
        {
            // 1. Try QueryFullProcessImageName with PROCESS_QUERY_LIMITED_INFORMATION first.
            // Works for unelevated callers querying user-session processes without throwing Access Denied.
            try
            {
                IntPtr hProc = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, p.Id);
                if (hProc != IntPtr.Zero)
                {
                    try
                    {
                        var sb = new System.Text.StringBuilder(1024);
                        int size = sb.Capacity;
                        if (QueryFullProcessImageName(hProc, 0, sb, ref size))
                        {
                            string result = sb.ToString(0, size);
                            if (!string.IsNullOrEmpty(result))
                                return result;
                        }
                    }
                    finally
                    {
                        CloseHandle(hProc);
                    }
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or UnauthorizedAccessException)
            {
                // Fall through to MainModule fallback
            }

            // 2. Fallback: MainModule (may throw for elevated processes)
            try
            {
                string? mmPath = p.MainModule?.FileName;
                if (!string.IsNullOrEmpty(mmPath))
                    return mmPath;
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
            }

            // 3. Fallback: Check Registry App Paths & known standard application paths
            return TryResolveAppPathByName(p.ProcessName);
        }

        public static string? TryResolveAppPathByName(string? processName)
        {
            if (string.IsNullOrWhiteSpace(processName))
                return null;

            if (AppPathLookupCache.TryGetValue(processName, out var cached))
                return cached;

            string? resolved = ResolveAppPathInternal(processName);
            AppPathLookupCache[processName] = resolved;
            return resolved;
        }

        private static string? ResolveAppPathInternal(string name)
        {
            string cleanName = Path.GetFileNameWithoutExtension(name.Trim('\"', ' '));
            string exeName = cleanName + ".exe";

            // 1. Check Registry App Paths (User scope first, then Machine scope)
            try
            {
                string subKey = $@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exeName}";
                using var cuKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subKey);
                if (cuKey != null)
                {
                    var val = cuKey.GetValue("") as string;
                    if (!string.IsNullOrEmpty(val))
                    {
                        string clean = Environment.ExpandEnvironmentVariables(val.Trim('\"', ' '));
                        if (File.Exists(clean)) return clean;
                    }
                }

                using var lmKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(subKey);
                if (lmKey != null)
                {
                    var val = lmKey.GetValue("") as string;
                    if (!string.IsNullOrEmpty(val))
                    {
                        string clean = Environment.ExpandEnvironmentVariables(val.Trim('\"', ' '));
                        if (File.Exists(clean)) return clean;
                    }
                }
            }
            catch { }

            // 2. Check WindowsApps (MSIX / Store Apps execution aliases)
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string winAppsExe = Path.Combine(localAppData, "Microsoft", "WindowsApps", exeName);
                if (File.Exists(winAppsExe)) return winAppsExe;
            }
            catch { }

            // 3. Known Standard Install Paths for Primary Applications
            try
            {
                string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

                string lower = cleanName.ToLowerInvariant();
                if (lower.Contains("chrome"))
                {
                    string p = Path.Combine(progFiles, @"Google\Chrome\Application\chrome.exe");
                    if (File.Exists(p)) return p;
                    p = Path.Combine(progFilesX86, @"Google\Chrome\Application\chrome.exe");
                    if (File.Exists(p)) return p;
                }
                else if (lower.Contains("edge") || lower == "msedge")
                {
                    string p = Path.Combine(progFilesX86, @"Microsoft\Edge\Application\msedge.exe");
                    if (File.Exists(p)) return p;
                    p = Path.Combine(progFiles, @"Microsoft\Edge\Application\msedge.exe");
                    if (File.Exists(p)) return p;
                }
                else if (lower.Contains("brave"))
                {
                    string p = Path.Combine(progFiles, @"BraveSoftware\Brave-Browser\Application\brave.exe");
                    if (File.Exists(p)) return p;
                }
                else if (lower == "code" || lower.Contains("vscode"))
                {
                    string p = Path.Combine(localAppData, @"Programs\Microsoft VS Code\Code.exe");
                    if (File.Exists(p)) return p;
                    p = Path.Combine(progFiles, @"Microsoft VS Code\Code.exe");
                    if (File.Exists(p)) return p;
                }
                else if (lower.Contains("discord"))
                {
                    string discordDir = Path.Combine(localAppData, "Discord");
                    if (Directory.Exists(discordDir))
                    {
                        var appDirs = Directory.GetDirectories(discordDir, "app-*");
                        if (appDirs.Length > 0)
                        {
                            Array.Sort(appDirs);
                            string p = Path.Combine(appDirs[^1], "Discord.exe");
                            if (File.Exists(p)) return p;
                        }
                        string updateExe = Path.Combine(discordDir, "Update.exe");
                        if (File.Exists(updateExe)) return updateExe;
                    }
                }
                else if (lower.Contains("spotify"))
                {
                    string p = Path.Combine(appData, @"Spotify\Spotify.exe");
                    if (File.Exists(p)) return p;
                    p = Path.Combine(localAppData, @"Microsoft\WindowsApps\Spotify.exe");
                    if (File.Exists(p)) return p;
                }
                else if (lower.Contains("telegram"))
                {
                    string p = Path.Combine(appData, @"Telegram Desktop\Telegram.exe");
                    if (File.Exists(p)) return p;
                }
                else if (lower.Contains("whatsapp"))
                {
                    string p = Path.Combine(localAppData, @"WhatsApp\WhatsApp.exe");
                    if (File.Exists(p)) return p;
                    p = Path.Combine(localAppData, @"Microsoft\WindowsApps\WhatsApp.exe");
                    if (File.Exists(p)) return p;
                }
                else if (lower.Contains("antigravity"))
                {
                    string p = Path.Combine(localAppData, @"Programs\Antigravity\Antigravity.exe");
                    if (File.Exists(p)) return p;
                }
                else if (lower.Contains("postman"))
                {
                    string p = Path.Combine(localAppData, @"Postman\Postman.exe");
                    if (File.Exists(p)) return p;
                }
                else if (lower == "vlc")
                {
                    string p = Path.Combine(progFiles, @"VideoLAN\VLC\vlc.exe");
                    if (File.Exists(p)) return p;
                    p = Path.Combine(progFilesX86, @"VideoLAN\VLC\vlc.exe");
                    if (File.Exists(p)) return p;
                }
                else if (lower.Contains("steam"))
                {
                    string p = Path.Combine(progFilesX86, @"Steam\steam.exe");
                    if (File.Exists(p)) return p;
                    p = Path.Combine(progFiles, @"Steam\steam.exe");
                    if (File.Exists(p)) return p;
                }
                else if (lower.Contains("notepad++"))
                {
                    string p = Path.Combine(progFiles, @"Notepad++\notepad++.exe");
                    if (File.Exists(p)) return p;
                    p = Path.Combine(progFilesX86, @"Notepad++\notepad++.exe");
                    if (File.Exists(p)) return p;
                }
                else if (lower == "wt" || lower.Contains("windowsterminal"))
                {
                    string p = Path.Combine(localAppData, @"Microsoft\WindowsApps\wt.exe");
                    if (File.Exists(p)) return p;
                }
            }
            catch { }

            // 4. Check System32 for standard system tools (taskmgr, calc, notepad, cmd, powershell)
            try
            {
                string sysDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
                string sysPath = Path.Combine(sysDir, exeName);
                if (File.Exists(sysPath)) return sysPath;
            }
            catch { }

            // 5. Check PATH environment variable
            try
            {
                string? pathEnv = Environment.GetEnvironmentVariable("PATH");
                if (!string.IsNullOrEmpty(pathEnv))
                {
                    foreach (var dir in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries))
                    {
                        try
                        {
                            string candidate = Path.Combine(dir.Trim(), exeName);
                            if (File.Exists(candidate)) return candidate;
                        }
                        catch { }
                    }
                }
            }
            catch { }

            return null;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        private const uint SHGFI_ICON = 0x000000100;
        private const uint SHGFI_LARGEICON = 0x000000000;
        private const uint WM_GETICON = 0x007F;
        private const int ICON_BIG = 1;
        private const int ICON_SMALL = 0;
        private const int ICON_SMALL2 = 2;
        private const uint SMTO_ABORTIFHUNG = 0x0002;

        public static ImageSource? ExtractAppIcon(string? commandOrPath, IntPtr hwnd = default)
        {
            try
            {
                string? cleanPath = null;
                if (!string.IsNullOrWhiteSpace(commandOrPath))
                {
                    string c = commandOrPath.Trim();
                    int exeIdx = c.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                    cleanPath = exeIdx > 0 ? c.Substring(0, exeIdx + 4).Trim('\"', ' ') : c.Trim('\"');
                    cleanPath = Environment.ExpandEnvironmentVariables(cleanPath);

                    if (!File.Exists(cleanPath))
                    {
                        cleanPath = TryResolveAppPathByName(Path.GetFileNameWithoutExtension(cleanPath));
                    }
                }

                // 1. Try extracting high-res 48x48 icon via PrivateExtractIcons first
                if (!string.IsNullOrEmpty(cleanPath) && File.Exists(cleanPath))
                {
                    try
                    {
                        var phicon = new IntPtr[1];
                        var piconid = new uint[1];
                        uint count = PrivateExtractIcons(cleanPath, 0, 48, 48, phicon, piconid, 1, 0);
                        if (count > 0 && phicon[0] != IntPtr.Zero)
                        {
                            try
                            {
                                var bmp = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                                    phicon[0],
                                    Int32Rect.Empty,
                                    BitmapSizeOptions.FromEmptyOptions());
                                bmp.Freeze();
                                return bmp;
                            }
                            finally
                            {
                                DestroyIcon(phicon[0]);
                            }
                        }
                    }
                    catch { }

                    // Fallback A: SHGetFileInfo (Standard 32x32 shell icon)
                    try
                    {
                        var shinfo = new SHFILEINFO();
                        IntPtr res = SHGetFileInfo(cleanPath, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), SHGFI_ICON | SHGFI_LARGEICON);
                        if (res != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
                        {
                            try
                            {
                                var bmp = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                                    shinfo.hIcon,
                                    Int32Rect.Empty,
                                    BitmapSizeOptions.FromEmptyOptions());
                                bmp.Freeze();
                                return bmp;
                            }
                            finally
                            {
                                DestroyIcon(shinfo.hIcon);
                            }
                        }
                    }
                    catch { }

                    // Fallback B: Icon.ExtractAssociatedIcon
                    try
                    {
                        using var sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(cleanPath);
                        if (sysIcon != null)
                        {
                            IntPtr hIcon = sysIcon.Handle;
                            try
                            {
                                var bmp = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                                    hIcon,
                                    Int32Rect.Empty,
                                    BitmapSizeOptions.FromEmptyOptions());
                                bmp.Freeze();
                                return bmp;
                            }
                            finally
                            {
                                DestroyIcon(hIcon);
                            }
                        }
                    }
                    catch { }
                }

                // 2. If path didn't yield an icon and we have an active HWND, query WM_GETICON or Window Class
                if (hwnd != IntPtr.Zero)
                {
                    IntPtr hIcon = IntPtr.Zero;
                    if (SendMessageTimeout(hwnd, WM_GETICON, (IntPtr)ICON_BIG, IntPtr.Zero, SMTO_ABORTIFHUNG, 250, out IntPtr res) != IntPtr.Zero && res != IntPtr.Zero)
                    {
                        hIcon = res;
                    }
                    else if (SendMessageTimeout(hwnd, WM_GETICON, (IntPtr)ICON_SMALL2, IntPtr.Zero, SMTO_ABORTIFHUNG, 250, out res) != IntPtr.Zero && res != IntPtr.Zero)
                    {
                        hIcon = res;
                    }
                    else if (SendMessageTimeout(hwnd, WM_GETICON, (IntPtr)ICON_SMALL, IntPtr.Zero, SMTO_ABORTIFHUNG, 250, out res) != IntPtr.Zero && res != IntPtr.Zero)
                    {
                        hIcon = res;
                    }
                    else
                    {
                        // Check Window Class Icons
                        IntPtr classIcon = GetClassLongPtr(hwnd, GCLP_HICON);
                        if (classIcon != IntPtr.Zero)
                        {
                            hIcon = classIcon;
                        }
                        else
                        {
                            IntPtr classIconSm = GetClassLongPtr(hwnd, GCLP_HICONSM);
                            if (classIconSm != IntPtr.Zero)
                            {
                                hIcon = classIconSm;
                            }
                        }
                    }

                    if (hIcon != IntPtr.Zero)
                    {
                        try
                        {
                            var bmp = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                                hIcon,
                                Int32Rect.Empty,
                                BitmapSizeOptions.FromEmptyOptions());
                            bmp.Freeze();
                            return bmp;
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Log($"ExtractAppIcon failed for '{commandOrPath}': {ex.Message}", "DEBUG");
            }
            return null;
        }

        public static ImageSource? ExtractIconFromCommand(string command)
        {
            return ExtractAppIcon(command, IntPtr.Zero);
        }

        public static (ImageSource? icon, string displayName) GetCachedProcessMetadata(string processName, string? exePath, IntPtr hwnd = default)
        {
            if (ProcessMetaCache.TryGetValue(processName, out var cached) && cached.icon != null)
            {
                return cached;
            }

            ImageSource? icon = null;
            string displayName = processName;

            try
            {
                if (string.IsNullOrEmpty(exePath))
                {
                    exePath = TryResolveAppPathByName(processName);
                }

                icon = ExtractAppIcon(exePath, hwnd);

                // Identify friendly application name first
                var identified = IdentifyApplication(processName, exePath, hwnd, null);
                if (identified.HasValue && !string.IsNullOrWhiteSpace(identified.Value.displayName))
                {
                    displayName = identified.Value.displayName;
                }
                else if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                {
                    try
                    {
                        var vi = FileVersionInfo.GetVersionInfo(exePath);
                        if (!string.IsNullOrWhiteSpace(vi.FileDescription))
                        {
                            displayName = vi.FileDescription.Trim();
                        }
                        else if (!string.IsNullOrWhiteSpace(vi.ProductName))
                        {
                            displayName = vi.ProductName.Trim();
                        }
                    }
                    catch
                    {
                        // FileVersionInfo may fail on certain sandboxed apps
                    }
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Log($"GetCachedProcessMetadata failed for {processName}: {ex.Message}", "DEBUG");
            }

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
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
                Log($"Failed to retrieve current session ID: {ex.Message}", "DEBUG");
            }

            Process[] processes = Array.Empty<Process>();
            try
            {
                processes = Process.GetProcesses();

                // Group by AppKey to aggregate all helper processes into the primary application
                var appMap = new Dictionary<string, (
                    string appKey, 
                    string displayName, 
                    string mainProc, 
                    double totalRam, 
                    int instances, 
                    HashSet<string> procs, 
                    string? mainPath, 
                    int mainPid,
                    bool hasActiveWindow,
                    IntPtr hwnd
                )>(StringComparer.OrdinalIgnoreCase);

                foreach (var p in processes)
                {
                    try
                    {
                        if (p.SessionId != currentSessionId)
                            continue;

                        string name = p.ProcessName;
                        if (string.IsNullOrWhiteSpace(name))
                            continue;

                        string? exePath = GetProcessExePath(p);
                        IntPtr hwnd = IntPtr.Zero;
                        string windowTitle = "";
                        try
                        {
                            hwnd = p.MainWindowHandle;
                            windowTitle = p.MainWindowTitle;
                        }
                        catch { }

                        // Filter out Windows services and background system daemons
                        if (IsSystemOrServiceProcess(name, exePath, hwnd, windowTitle))
                            continue;

                        double wsMb = p.WorkingSet64 / (1024.0 * 1024.0);

                        var match = IdentifyApplication(name, exePath, hwnd, windowTitle);
                        if (match == null)
                            continue;

                        string appKey = match.Value.appKey;
                        string dispName = match.Value.displayName;
                        bool hasWindow = hwnd != IntPtr.Zero && !string.IsNullOrWhiteSpace(windowTitle);

                        if (!appMap.TryGetValue(appKey, out var appData))
                        {
                            appData = (appKey, dispName, name, 0.0, 0, new HashSet<string>(StringComparer.OrdinalIgnoreCase), exePath, p.Id, hasWindow, hwnd);
                        }

                        appData.totalRam += wsMb;
                        appData.instances += 1;
                        appData.procs.Add(name);
                        if (hasWindow) appData.hasActiveWindow = true;
                        if (appData.hwnd == IntPtr.Zero && hwnd != IntPtr.Zero) appData.hwnd = hwnd;
                        if (string.IsNullOrEmpty(appData.mainPath) && !string.IsNullOrEmpty(exePath))
                        {
                            appData.mainPath = exePath;
                        }

                        appMap[appKey] = appData;
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
                }

                var result = new List<RunningProcessItem>();
                foreach (var kvp in appMap.Values)
                {
                    double ramMb = Math.Round(kvp.totalRam, 1);
                    double ramPct = totalRamMb > 0 ? Math.Round((ramMb / totalRamMb) * 100.0, 1) : 0;
                    string ramFormatted = ramMb >= 1024.0
                        ? $"{(ramMb / 1024.0):F2} GB"
                        : $"{ramMb:F1} MB";

                    var (icon, cachedDisplayName) = GetCachedProcessMetadata(kvp.mainProc, kvp.mainPath, kvp.hwnd);
                    string displayName = !string.IsNullOrWhiteSpace(cachedDisplayName) && !cachedDisplayName.Equals(kvp.mainProc, StringComparison.OrdinalIgnoreCase)
                        ? cachedDisplayName
                        : kvp.displayName;

                    result.Add(new RunningProcessItem
                    {
                        AppKey = kvp.appKey,
                        DisplayName = displayName,
                        MainProcessName = kvp.mainProc,
                        RamMb = ramMb,
                        RamFormatted = ramFormatted,
                        RamPercentOfTotal = ramPct,
                        InstanceCount = kvp.instances,
                        AssociatedProcesses = kvp.procs.ToList(),
                        IconSource = icon,
                        MainPid = kvp.mainPid,
                        MainPath = kvp.mainPath ?? "",
                        HasActiveWindow = kvp.hasActiveWindow
                    });
                }

                return result.OrderByDescending(x => x.RamMb).ToList();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Log($"GetAllRunningProcesses failed: {ex.Message}", "DEBUG");
                return new List<RunningProcessItem>();
            }
            finally
            {
                foreach (var p in processes)
                {
                    try { p.Dispose(); } catch (Exception ex) when (ex is not OutOfMemoryException) { }
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
                            long memBefore = 0;
                            try { memBefore = p.WorkingSet64; } catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }

                            // 1. Try graceful window close first if process has an open window
                            bool closedGracefully = false;
                            try
                            {
                                if (p.MainWindowHandle != IntPtr.Zero)
                                {
                                    p.CloseMainWindow();
                                    closedGracefully = p.WaitForExit(350);
                                }
                            }
                            catch { }

                            // 2. If not closed gracefully or is background app, kill entire process tree
                            if (!closedGracefully && !p.HasExited)
                            {
                                p.Kill(entireProcessTree: true);
                                p.WaitForExit(500);
                            }

                            freedMb += memBefore / (1024.0 * 1024.0);
                            killedCount++;
                        }
                        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                        {
                            Log($"Failed to kill process {processName}: {ex.Message}", "WARN");
                        }
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                {
                    Log($"Failed to get processes for {processName}: {ex.Message}", "WARN");
                }
                finally
                {
                    foreach (var p in procs)
                    {
                        try { p.Dispose(); } catch (Exception ex) when (ex is not OutOfMemoryException) { }
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
            catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
            {
                Log($"IsAppEnabledInWindows registry check failed for {appName}: {ex.Message}", "WARN");
            }

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
            catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
            {
                Log($"CheckApprovedKey failed for {appName} at {subPath}: {ex.Message}", "DEBUG");
            }
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
                        catch (Exception ex) when (ex is not OutOfMemoryException)
                        {
                            Log($"GetStartupApps path parse failed for '{cmd}': {ex.Message}", "DEBUG");
                        }

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
                catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
                {
                    Log($"GetStartupApps ReadKey failed for {loc}: {ex.Message}", "WARN");
                }
            }

            ReadKey(RegistryHive.CurrentUser, RegistryView.Default, "HKCU", true);
            ReadKey(RegistryHive.LocalMachine, RegistryView.Registry64, "HKLM-64", false);
            ReadKey(RegistryHive.LocalMachine, RegistryView.Registry32, "HKLM-32", false);

            return apps;
        }

        public bool ToggleStartupApp(StartupAppItem app)
        {
            if (app.IsSystemManaged)
            {
                Log($"ToggleStartupApp blocked for system-managed app: {app.Name}", "WARN");
                return false;
            }

            try
            {
                byte targetByte = app.IsEnabled ? (byte)0x03 : (byte)0x02; // 0x03 = Disabled, 0x02 = Enabled
                byte[] newBytes = new byte[] { targetByte, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

                if (app.IsUserScope)
                {
                    const string approvedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
                    using (var readKey = Registry.CurrentUser.OpenSubKey(approvedPath, false))
                    {
                        if (readKey != null)
                        {
                            var val = readKey.GetValue(app.Name);
                            if (val is byte[] b && b.Length > 0 && b[0] == 0x06)
                            {
                                Log($"ToggleStartupApp blocked for system-managed app (0x06 in HKCU): {app.Name}", "WARN");
                                return false;
                            }
                        }
                    }

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

                    using (var hklm64Read = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                    using (var readKey = hklm64Read.OpenSubKey(subPath, false))
                    {
                        if (readKey != null)
                        {
                            var val = readKey.GetValue(app.Name);
                            if (val is byte[] b && b.Length > 0 && b[0] == 0x06)
                            {
                                Log($"ToggleStartupApp blocked for system-managed app (0x06 in HKLM): {app.Name}", "WARN");
                                return false;
                            }
                        }
                    }

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
            catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
            {
                Log($"ToggleStartupApp failed for {app.Name}: {ex.Message}", "ERROR");
            }
            return false;
        }

        private static bool WriteHklmStartupApprovedElevated(string subPath, string appName, byte[] bytes, bool elevate = true)
        {
            try
            {
                // Strict validation: reject any null, empty, or control characters in appName
                if (string.IsNullOrWhiteSpace(appName) || appName.Any(char.IsControl))
                    return false;

                if (bytes == null || bytes.Length != 12 || bytes[0] == 0x06)
                    return false;

                // Transfer all arguments as Base64 strings to prevent any shell/script command injection
                string b64SubPath = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(subPath));
                string b64AppName = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(appName));
                string b64Bytes = Convert.ToBase64String(bytes);

                if (!elevate)
                {
                    try
                    {
                        using var hklm64 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                        using var key = hklm64.CreateSubKey(subPath, true);
                        if (key != null)
                        {
                            key.SetValue(appName, bytes, RegistryValueKind.Binary);
                            return true;
                        }
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException) { }
                    return false;
                }

                var (success, _) = RelaunchSelfElevated($"--set-approved {b64SubPath} {b64AppName} {b64Bytes}");
                return success;
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
            {
                Log($"WriteHklmStartupApprovedElevated failed for {appName}: {ex.Message}", "ERROR");
            }
            return false;
        }

        private static (bool Success, bool UserCancelled) RelaunchSelfElevated(string arguments)
        {
            try
            {
                string exePath = App.GetOwnExePath();
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = arguments,
                    UseShellExecute = true,
                    Verb = "runas"
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    proc.WaitForExit(8000);
                    return (proc.ExitCode == 0, false);
                }
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // Error 1223: The operation was canceled by the user (UAC prompt denied)
                return (false, true);
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
            {
                Log($"RelaunchSelfElevated failed: {ex.Message}", "ERROR");
            }
            return (false, false);
        }

        public static string ExtractProcessBaseName(string command, string name)
        {
            if (!string.IsNullOrWhiteSpace(command))
            {
                try
                {
                    string cmd = command.Trim();
                    if (cmd.StartsWith("\""))
                    {
                        int endQuote = cmd.IndexOf('"', 1);
                        if (endQuote > 1)
                        {
                            string exePath = cmd.Substring(1, endQuote - 1);
                            return Path.GetFileNameWithoutExtension(exePath).ToLowerInvariant();
                        }
                    }
                    else
                    {
                        int space = cmd.IndexOf(' ');
                        string exePath = space > 0 ? cmd.Substring(0, space) : cmd;
                        return Path.GetFileNameWithoutExtension(exePath).ToLowerInvariant();
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    Log($"ExtractProcessBaseName failed for command '{command}': {ex.Message}", "DEBUG");
                }
            }
            try
            {
                return Path.GetFileNameWithoutExtension(name).Trim().ToLowerInvariant();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return name.Trim().ToLowerInvariant();
            }
        }

        public static StartupImpactLevel ClassifyImpactFromMetrics(long cpuMs, long diskBytes)
        {
            // Task Manager parity:
            // High: CPU > 1000ms OR Disk > 3MB
            // Medium: CPU 300–1000ms OR Disk 300KB–3MB
            // Low: rest
            if (cpuMs > 1000 || diskBytes > 3 * 1024 * 1024)
            {
                return StartupImpactLevel.High;
            }
            if (cpuMs >= 300 || diskBytes >= 300 * 1024)
            {
                return StartupImpactLevel.Medium;
            }
            return StartupImpactLevel.Low;
        }

        private static BootMeasureCache? LoadBootMeasureCache()
        {
            try
            {
                string progDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Tempo", "boot-measure.json");
                string appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tempo", "boot-measure.json");

                string targetPath = File.Exists(progDataPath) ? progDataPath : (File.Exists(appDataPath) ? appDataPath : "");
                if (string.IsNullOrEmpty(targetPath)) return null;

                string json = File.ReadAllText(targetPath);
                var cache = JsonSerializer.Deserialize<BootMeasureCache>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (cache != null)
                {
                    return cache;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or JsonException)
            {
                Log($"LoadBootMeasureCache read failed: {ex.Message}", "DEBUG");
            }
            return null;
        }

        public BootMeasureResult MeasureBootPerformanceElevated()
        {
            try
            {
                var (success, userCancelled) = RelaunchSelfElevated("--measure-boot");
                if (userCancelled)
                {
                    Log("MeasureBootPerformanceElevated cancelled by user (UAC).", "INFO");
                    return BootMeasureResult.UserCancelledUac;
                }
                return success ? BootMeasureResult.Success : BootMeasureResult.Failed;
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
            {
                Log($"MeasureBootPerformanceElevated failed: {ex.Message}", "ERROR");
            }
            return BootMeasureResult.Failed;
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
                    double? val = null;
                    if (fwVal is int fwInt && fwInt > 0)
                    {
                        val = Math.Round(fwInt / 1000.0, 1);
                    }
                    else if (fwVal is long fwLong && fwLong > 0)
                    {
                        val = Math.Round(fwLong / 1000.0, 1);
                    }

                    if (val.HasValue && val.Value >= 0.0 && val.Value <= 120.0)
                    {
                        info.BiosTimeSeconds = val.Value;
                    }
                    else
                    {
                        info.BiosTimeSeconds = null;
                    }
                }
            }
            catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
            {
                Log($"GetBootPerformanceInfo registry read failed: {ex.Message}", "DEBUG");
            }

            try
            {
                info.Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Log($"GetBootPerformanceInfo uptime read failed: {ex.Message}", "DEBUG");
            }

            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT LastBootUpTime FROM Win32_OperatingSystem");
                using var coll = searcher.Get();
                foreach (ManagementObject mo in coll)
                {
                    using (mo)
                    {
                        if (mo["LastBootUpTime"] is string bootStr)
                        {
                            info.LastBootUpTime = ManagementDateTimeConverter.ToDateTime(bootStr);
                            break;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Log($"GetBootPerformanceInfo WMI LastBootUpTime read failed: {ex.Message}", "DEBUG");
            }

            double? unprivilegedMainPathSeconds = null;
            try
            {
                string query = "*[System[(EventID=100)]]";
                var logQuery = new EventLogQuery("Microsoft-Windows-Diagnostics-Performance/Operational", PathType.LogName, query)
                {
                    ReverseDirection = true
                };
                using var reader = new EventLogReader(logQuery);
                EventRecord? record = reader.ReadEvent();
                if (record != null)
                {
                    using (record)
                    {
                        string xml = record.ToXml();
                        int idx = xml.IndexOf("MainPathBootTime", StringComparison.OrdinalIgnoreCase);
                        if (idx >= 0)
                        {
                            int start = xml.IndexOf('>', idx);
                            int end = xml.IndexOf('<', start);
                            if (start >= 0 && end > start)
                            {
                                string val = xml.Substring(start + 1, end - start - 1);
                                if (long.TryParse(val, out long ms) && ms > 0)
                                {
                                    double sec = Math.Round(ms / 1000.0, 1);
                                    if (sec >= 0.0 && sec <= 600.0)
                                    {
                                        unprivilegedMainPathSeconds = sec;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is EventLogNotFoundException or EventLogException or EventLogReadingException or UnauthorizedAccessException or SecurityException or IOException)
            {
                Log($"GetBootPerformanceInfo unprivileged Event 100 read: {ex.Message}", "DEBUG");
            }

            var apps = startupApps ?? GetStartupApps();
            info.ActiveStartupAppsCount = apps.Count(a => a.IsEnabled);
            info.DisabledStartupAppsCount = apps.Count(a => !a.IsEnabled);

            var cache = LoadBootMeasureCache();
            if (cache != null)
            {
                info.IsMeasured = true;

                // Staleness check:
                // Stale if:
                // 1. cache is older than 14 days
                // 2. cache timestamp < LastBootUpTime (system rebooted since last measurement)
                // 3. BootId is missing or doesn't match current boot session
                bool isStale = false;
                if (cache.Timestamp < DateTime.UtcNow.AddDays(-14))
                {
                    isStale = true;
                }
                else if (info.LastBootUpTime.HasValue && cache.Timestamp < info.LastBootUpTime.Value)
                {
                    isStale = true;
                }
                else if (string.IsNullOrEmpty(cache.BootId) || (info.LastBootUpTime.HasValue && !string.Equals(cache.BootId, info.LastBootUpTime.Value.ToUniversalTime().ToString("o"), StringComparison.OrdinalIgnoreCase)))
                {
                    isStale = true;
                }
                info.IsCacheStale = isStale;

                if (cache.MainPathBootMs > 0)
                {
                    double sec = Math.Round(cache.MainPathBootMs / 1000.0, 1);
                    if (sec >= 0.0 && sec <= 600.0)
                    {
                        info.MainPathBootSeconds = sec;
                    }
                }
                else if (unprivilegedMainPathSeconds.HasValue)
                {
                    info.MainPathBootSeconds = unprivilegedMainPathSeconds;
                }

                foreach (var app in apps)
                {
                    string baseName = ExtractProcessBaseName(app.Command, app.Name);
                    var match = cache.Apps.FirstOrDefault(c =>
                        string.Equals(c.ProcessName, baseName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(c.ProcessName, app.Name, StringComparison.OrdinalIgnoreCase));

                    if (match != null)
                    {
                        app.HasRealMeasurement = true;
                        app.MeasuredCpuMs = match.CpuMs;
                        app.MeasuredDiskBytes = match.DiskBytes;
                        app.Impact = app.IsEnabled ? ClassifyImpactFromMetrics(match.CpuMs, match.DiskBytes) : StartupImpactLevel.Disabled;
                    }
                    else
                    {
                        app.HasRealMeasurement = true;
                        app.MeasuredCpuMs = null;
                        app.MeasuredDiskBytes = null;
                        app.Impact = app.IsEnabled ? StartupImpactLevel.Low : StartupImpactLevel.Disabled;
                    }
                }

                info.ActiveAppsDelaySeconds = Math.Round(apps.Where(a => a.IsEnabled && a.MeasuredCpuMs.HasValue).Sum(a => a.MeasuredCpuMs!.Value) / 1000.0, 1);
            }
            else
            {
                info.IsMeasured = false;
                info.IsCacheStale = false;
                info.MainPathBootSeconds = unprivilegedMainPathSeconds;
                info.ActiveAppsDelaySeconds = null;
            }

            if (info.MainPathBootSeconds.HasValue && (info.MainPathBootSeconds.Value < 0.0 || info.MainPathBootSeconds.Value > 600.0))
            {
                info.MainPathBootSeconds = null;
            }

            if (info.IsMeasured && info.MainPathBootSeconds.HasValue)
            {
                if (info.MainPathBootSeconds.Value <= 20.0)
                {
                    info.Rating = "Fast";
                    info.RatingText = LocalizationManager.CurrentLanguage == "ar" ? "ممتاز وسريع" : "Fast & Optimal";
                }
                else if (info.MainPathBootSeconds.Value <= 35.0)
                {
                    info.Rating = "Moderate";
                    info.RatingText = LocalizationManager.CurrentLanguage == "ar" ? "جيد ومقبول" : "Moderate";
                }
                else
                {
                    info.Rating = "Slow";
                    info.RatingText = LocalizationManager.CurrentLanguage == "ar" ? "يحتاج تحسين" : "Needs Optimization";
                }

                if (apps.Any(a => a.IsEnabled && (a.Impact == StartupImpactLevel.High || a.Impact == StartupImpactLevel.Medium)))
                {
                    info.Recommendation = LocalizationManager.CurrentLanguage == "ar"
                        ? "تعطيل التطبيقات ذات الأثر المرتفع يحسن سرعة بدء تشغيل النظام."
                        : "Disabling high-impact startup applications helps improve system boot speed.";
                }
                else
                {
                    info.Recommendation = LocalizationManager.CurrentLanguage == "ar"
                        ? "قائمة برامج بدء التشغيل محسنة ومثالية لأقصى سرعة إقلاع."
                        : "Your startup list is cleanly optimized for maximum boot speed.";
                }
            }
            else
            {
                info.Rating = "";
                info.RatingText = LocalizationManager.GetString("BootNA", LocalizationManager.CurrentLanguage == "ar" ? "غير متوفر" : "N/A");
                info.Recommendation = LocalizationManager.CurrentLanguage == "ar"
                    ? "قائمة بدء التشغيل: تعطيل التطبيقات غير الضرورية التي تعمل في الخلفية يساعد على تحسين استجابة الإقلاع."
                    : "Startup list: disabling non-essential background applications helps improve boot responsiveness.";
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
            catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
            {
                Log($"IsRunAtStartupEnabled failed: {ex.Message}", "WARN");
                return false;
            }
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
            catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
            {
                Log($"SetRunAtStartup({enable}) failed: {ex.Message}", "WARN");
                return false;
            }
        }

        public void Dispose()
        {
            try { _cpuCounter?.Dispose(); } catch (Exception ex) when (ex is not OutOfMemoryException) { }
            if (_isComputerOpen && _computer != null)
            {
                try
                {
                    _computer.Close();
                    _isComputerOpen = false;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    Log($"_computer.Close() during Dispose failed: {ex.Message}", "DEBUG");
                }
            }
        }
    }
}
