using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Security;
using System.ComponentModel;
using Tempo.Services;
using Tempo.Models;

// Disambiguate System.Windows.Forms types
using TrayNotifyIcon = System.Windows.Forms.NotifyIcon;
using TrayContextMenuStrip = System.Windows.Forms.ContextMenuStrip;
using TrayToolStripSeparator = System.Windows.Forms.ToolStripSeparator;
using TrayMouseButtons = System.Windows.Forms.MouseButtons;

namespace Tempo
{
    public enum DockPosition
    {
        TopToolbar,
        Side
    }

    public enum AppViewMode
    {
        Dashboard,
        Toolbar
    }

    public class StorageDriveViewModel
    {
        public string DriveLetter { get; set; } = "";
        public string VolumeLabel { get; set; } = "";
        public double TotalGb { get; set; }
        public double FreeGb { get; set; }
        public double UsedPercent { get; set; }
        public string MediaType { get; set; } = "Unknown";
        public string SpaceSummary => LocalizationManager.FormatStorageSummary(FreeGb, TotalGb, UsedPercent);

        public bool IsUnknown => MediaType.Equals("Unknown", StringComparison.OrdinalIgnoreCase);
        public bool IsTrimEnabled => !IsUnknown;
        public string ActionButtonTooltip => IsUnknown
            ? (LocalizationManager.CurrentLanguage == "ar" ? "نوع القرص غير معروف — تم تعطيل TRIM" : "Unknown drive type — TRIM disabled")
            : "";

        public Visibility TrimVisibility => Visibility.Visible;
        public string ActionButtonText => MediaType.Equals("HDD", StringComparison.OrdinalIgnoreCase)
            ? (LocalizationManager.CurrentLanguage == "ar" ? "إلغاء التجزئة" : "Defrag")
            : (LocalizationManager.CurrentLanguage == "ar" ? "تنشيط TRIM" : "Run TRIM");

        public Brush BadgeBg => MediaType switch
        {
            "SSD" => new SolidColorBrush(Color.FromArgb(40, 0, 102, 255)),
            "HDD" => new SolidColorBrush(Color.FromArgb(40, 68, 221, 193)),
            _ => new SolidColorBrush(Color.FromArgb(40, 140, 150, 165))
        };
        public Brush BadgeBorder => MediaType switch
        {
            "SSD" => new SolidColorBrush(Color.FromArgb(80, 0, 102, 255)),
            "HDD" => new SolidColorBrush(Color.FromArgb(80, 68, 221, 193)),
            _ => new SolidColorBrush(Color.FromArgb(80, 140, 150, 165))
        };
        public Brush BadgeFg => MediaType switch
        {
            "SSD" => new SolidColorBrush(Color.FromArgb(255, 179, 197, 255)),
            "HDD" => new SolidColorBrush(Color.FromArgb(255, 68, 221, 193)),
            _ => new SolidColorBrush(Color.FromArgb(255, 160, 170, 185))
        };

        public Brush ProgressBrush => UsedPercent switch
        {
            > 85 => new SolidColorBrush(Color.FromArgb(255, 255, 85, 85)),
            > 70 => new SolidColorBrush(Color.FromArgb(255, 255, 183, 125)),
            _ => new SolidColorBrush(Color.FromArgb(255, 68, 221, 193))
        };
    }

    public class ProcessViewModel
    {
        public string ProcessName { get; set; } = "";
        public double RamMb { get; set; }
        public string RamFormatted => LocalizationManager.FormatMb(RamMb);
    }

    public class AppSettings
    {
        public bool IsToolbarEnabled { get; set; } = true;
        public DockPosition ToolbarDock { get; set; } = DockPosition.TopToolbar;
        public string SelectedLanguage { get; set; } = "en"; // Default is English!
    }

    public partial class MainWindow : Window
    {
        private static readonly object _errorLogLock = new();
        private const long MaxLogSizeBytes = 5 * 1024 * 1024; // 5 MB rotation threshold
        private static void LogError(string context, Exception ex)
        {
            try
            {
                lock (_errorLogLock)
                {
                    string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tempo");
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    string logFile = Path.Combine(dir, "error.log");

                    var fi = new FileInfo(logFile);
                    if (fi.Exists && fi.Length > MaxLogSizeBytes)
                    {
                        string oldLog = logFile + ".old";
                        File.Move(logFile, oldLog, overwrite: true);
                    }

                    string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [ERROR] [{context}] ({ex.GetType().Name}): {ex.Message}";
                    File.AppendAllText(logFile, line + Environment.NewLine);
                }
            }
            catch (Exception logEx) when (logEx is IOException or UnauthorizedAccessException or SecurityException) { }
        }

        public readonly HardwareMonitorService _hardwareMonitor;
        public readonly CleanupService _cleanupService;
        private readonly DispatcherTimer _telemetryTimer;
        private readonly DispatcherTimer _autoHideTimer;
        private readonly DispatcherTimer _processesRefreshTimer;
        private readonly UpdateService _updateService = new UpdateService();
        private UpdateInfo? _availableUpdate;
        private TrayNotifyIcon? _trayIcon;

        public AppViewMode _currentView { get; private set; } = AppViewMode.Dashboard;
        public bool _isToolbarEnabled { get; set; } = true;
        public DockPosition _toolbarDock { get; set; } = DockPosition.TopToolbar;
        public string _selectedLanguage { get; set; } = "en";
        private bool _isPeeked = false;
        private bool _isFetching = false;
        private bool _isPinned = false;
        private int _telemetryTickCount = 0;

        private const double DashboardWidth = 440.0;
        private const double DashboardHeight = 700.0;
        private const double BarWidthH = 570.0;
        private const double BarHeightH = 42.0;
        private const double BarWidthV = 34.0;
        private const double BarHeightV = 315.0;

        public MainWindow()
        {
            InitializeComponent();

            LoadAppIconAndLogos();

            _hardwareMonitor = new HardwareMonitorService();
            _cleanupService = new CleanupService();

            this.Closed += Window_Closed;

            LoadSettings();
            LocalizationManager.Initialize(_selectedLanguage);
            LocalizationManager.LanguageChanged += OnLanguageChanged;
            InitSystemTray();

            // 1. Hardware Telemetry Polling (1.5s interval, fully paused when minimized)
            _telemetryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            _telemetryTimer.Tick += (s, e) => {
                if (this.WindowState != WindowState.Minimized && this.Visibility == Visibility.Visible)
                {
                    FetchTelemetryAsync();
                }
            };
            _telemetryTimer.Start();

            // Smart Apps Periodic Refresh Timer (4s interval, only active when Apps tab is open and user not searching)
            _processesRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            _processesRefreshTimer.Tick += (s, e) => {
                if (this.WindowState != WindowState.Minimized &&
                    this.Visibility == Visibility.Visible &&
                    PanelProcesses.Visibility == Visibility.Visible &&
                    string.IsNullOrWhiteSpace(_processSearchFilter) &&
                    !_isLoadingProcesses)
                {
                    LoadRunningProcessesFast();
                }
            };

            // Pause polling completely during minimize (0% idle CPU)
            this.StateChanged += (s, e) =>
            {
                if (this.WindowState == WindowState.Minimized)
                {
                    _telemetryTimer.Stop();
                    _processesRefreshTimer.Stop();
                }
                else if (this.WindowState == WindowState.Normal)
                {
                    _telemetryTimer.Start();
                    if (PanelProcesses.Visibility == Visibility.Visible)
                    {
                        _processesRefreshTimer.Start();
                    }
                    FetchTelemetryAsync();
                }
            };

            // 2. Auto-Hide Timer for Companion Toolbar (2.5s idle)
            _autoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            _autoHideTimer.Tick += AutoHideTimer_Tick;

            // 3. Window Dragging with Button-Click Isolation
            DashboardHeader.PreviewMouseLeftButtonDown += (s, e) =>
            {
                if (e.OriginalSource is DependencyObject dep)
                {
                    DependencyObject? current = dep;
                    while (current != null && current != DashboardHeader)
                    {
                        if (current is Button) return; // Allow button click
                        current = VisualTreeHelper.GetParent(current);
                    }
                }

                if (e.ClickCount == 2)
                {
                    PositionAtBottomRight();
                    SaveWindowPosition();
                }
                else if (e.ChangedButton == MouseButton.Left)
                {
                    try { this.DragMove(); } catch (InvalidOperationException) { }
                    SaveWindowPosition();
                }
            };

            CompanionBarHorizontal.PreviewMouseLeftButtonDown += CompanionBar_PreviewMouseLeftButtonDown;
            CompanionBarHorizontal.MouseEnter += CompanionBar_MouseEnter;
            CompanionBarHorizontal.MouseLeave += CompanionBar_MouseLeave;

            CompanionBarVertical.PreviewMouseLeftButtonDown += CompanionBar_PreviewMouseLeftButtonDown;
            CompanionBarVertical.MouseEnter += CompanionBar_MouseEnter;
            CompanionBarVertical.MouseLeave += CompanionBar_MouseLeave;

            // 4. Default: Open in Bottom-Right corner
            ShowDashboardView();

            // 5. Initial fast data load (All asynchronous)
            FetchTelemetryAsync();
            LoadStorageDrivesFast();
            LoadRecycleBinInfo();

            // 6. Check for updates in background (Cached every 6-12 hours)
            _ = CheckForUpdatesBackgroundAsync(force: false);

            // 7. Apply Language & Layout Alignment (English Default or Saved Language)
            ApplyLanguageUi(_selectedLanguage);
        }

        private void LoadAppIconAndLogos()
        {
            bool isAr = (LocalizationManager.CurrentLanguage == "ar");
            if (TxtAboutVersion != null)
            {
                TxtAboutVersion.Text = isAr
                    ? $"الإصدار {UpdateService.GetCurrentVersion()} (ويندوز x64)"
                    : $"Version {UpdateService.GetCurrentVersion()} (Windows Native x64)";
            }
            if (TxtAboutAuthor != null)
            {
                TxtAboutAuthor.Text = isAr
                    ? "تصميم وتطوير: م. عبدالرحمن إمام"
                    : "Designed & Engineered by Eng. Abdelrahman Emam";
            }

            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string icoPath = Path.Combine(baseDir, "app.ico");
                if (File.Exists(icoPath))
                {
                    this.Icon = BitmapFrame.Create(new Uri(icoPath, UriKind.Absolute));
                }

                string pngPath = Path.Combine(baseDir, "app.png");
                if (File.Exists(pngPath))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(pngPath, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();

                    if (ImgHeaderLogo != null) ImgHeaderLogo.Source = bmp;
                    if (ImgToolbarLogoH != null) ImgToolbarLogoH.Source = bmp;
                    if (ImgToolbarLogoV != null) ImgToolbarLogoV.Source = bmp;
                    if (ImgAboutLogo != null) ImgAboutLogo.Source = bmp;
                }
            }
            catch (Exception ex)
            {
                LogError("LoadAppIconAndLogos", ex);
            }
        }

        #region Multi-Monitor Win32 P/Invoke & Work Area

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MONITORINFO
        {
            public int cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, [In, Out] MONITORINFO lpmi);

        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

        public (double left, double top, double right, double bottom, double width, double height) GetCurrentMonitorWorkArea()
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    IntPtr hMonitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                    if (hMonitor != IntPtr.Zero)
                    {
                        var mi = new MONITORINFO();
                        if (GetMonitorInfo(hMonitor, mi))
                        {
                            var dpi = VisualTreeHelper.GetDpi(this);
                            double dpiX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
                            double dpiY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;

                            double left = mi.rcWork.Left / dpiX;
                            double top = mi.rcWork.Top / dpiY;
                            double right = mi.rcWork.Right / dpiX;
                            double bottom = mi.rcWork.Bottom / dpiY;
                            double width = right - left;
                            double height = bottom - top;

                            return (left, top, right, bottom, width, height);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("GetCurrentMonitorWorkArea", ex);
            }

            return (
                SystemParameters.WorkArea.Left,
                SystemParameters.WorkArea.Top,
                SystemParameters.WorkArea.Right,
                SystemParameters.WorkArea.Bottom,
                SystemParameters.WorkArea.Width,
                SystemParameters.WorkArea.Height
            );
        }

        #endregion

        #region Window Positioning & Persistence (position.json)

        public string GetPositionFilePath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dir = Path.Combine(appData, "Tempo");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return Path.Combine(dir, "position.json");
        }

        public void SaveWindowPosition()
        {
            if (_currentView != AppViewMode.Dashboard) return;
            try
            {
                var pos = new { X = this.Left, Y = this.Top };
                string json = JsonSerializer.Serialize(pos, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(GetPositionFilePath(), json);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or JsonException)
            {
                LogError("SaveWindowPosition", ex);
            }
        }

        public void RestoreSavedDashboardPosition()
        {
            try
            {
                string path = GetPositionFilePath();
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var pos = JsonSerializer.Deserialize<Dictionary<string, double>>(json);
                    if (pos != null && pos.TryGetValue("X", out double x) && pos.TryGetValue("Y", out double y))
                    {
                        var wa = GetCurrentMonitorWorkArea();
                        // Clamp coordinates so window is guaranteed to be fully on-screen
                        double clampedX = Math.Max(wa.left, Math.Min(x, wa.right - DashboardWidth));
                        double clampedY = Math.Max(wa.top, Math.Min(y, wa.bottom - DashboardHeight));
                        this.Left = clampedX;
                        this.Top = clampedY;
                        return;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or JsonException)
            {
                LogError("RestoreSavedDashboardPosition", ex);
            }

            PositionAtBottomRight();
        }

        public void PositionAtBottomRight()
        {
            this.Width = DashboardWidth;
            this.Height = DashboardHeight;

            var wa = GetCurrentMonitorWorkArea();
            const double margin = 6.0;

            this.Left = wa.right - DashboardWidth - margin;
            this.Top = wa.bottom - DashboardHeight - margin;
            SaveWindowPosition();
        }

        #endregion

        #region Window Controls

        public void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        public void BtnCloseApp_Click(object sender, RoutedEventArgs e)
        {
            ExitApp();
        }

        private void OnLanguageChanged(string lang) => ApplyLanguageUi(lang);

        private void Window_Closed(object? sender, EventArgs e)
        {
            LocalizationManager.LanguageChanged -= OnLanguageChanged;
            _telemetryTimer?.Stop();
            _processesRefreshTimer?.Stop();
            _autoHideTimer?.Stop();
            _toastTimer?.Stop();

            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }

            try { _hardwareMonitor?.Dispose(); } catch (Exception ex) when (ex is not OutOfMemoryException) { LogError("DisposeHardwareMonitor", ex); }
        }

        public void ExitApp()
        {
            // Immediate visual vanishing (<1ms)
            this.Hide();

            Window_Closed(this, EventArgs.Empty);

            // Graceful WPF Application Shutdown (DEF-17)
            System.Windows.Application.Current.Shutdown();
        }

        #endregion

        #region View Switching & Navigation Tabs (100% Instant, Zero Lag)

        public void ShowDashboardView()
        {
            _currentView = AppViewMode.Dashboard;
            _autoHideTimer.Stop();

            this.BeginAnimation(LeftProperty, null);
            this.BeginAnimation(TopProperty, null);
            this.BeginAnimation(OpacityProperty, null);

            CompanionBarHorizontal.Visibility = Visibility.Collapsed;
            CompanionBarVertical.Visibility = Visibility.Collapsed;
            MainDashboardView.Visibility = Visibility.Visible;
            this.ShowInTaskbar = true;
            this.Topmost = false;
            this.Opacity = 1.0;
            this.Width = DashboardWidth;
            this.Height = DashboardHeight;

            RestoreSavedDashboardPosition();
            SelectTab("Overview");
            UpdateToolbarSettingsUI();
        }

        public void ShowToolbarView()
        {
            if (!_isToolbarEnabled)
            {
                this.WindowState = WindowState.Minimized;
                return;
            }

            if (_currentView == AppViewMode.Dashboard)
            {
                SaveWindowPosition();
            }

            this.BeginAnimation(LeftProperty, null);
            this.BeginAnimation(TopProperty, null);
            this.BeginAnimation(OpacityProperty, null);

            _toastTimer?.Stop();
            if (ToastBanner != null) ToastBanner.Visibility = Visibility.Collapsed;

            _currentView = AppViewMode.Toolbar;
            MainDashboardView.Visibility = Visibility.Collapsed;
            this.ShowInTaskbar = false;
            this.Topmost = true;
            this.Opacity = 1.0;

            var wa = GetCurrentMonitorWorkArea();

            if (_toolbarDock == DockPosition.Side)
            {
                CompanionBarHorizontal.Visibility = Visibility.Collapsed;
                CompanionBarVertical.Visibility = Visibility.Visible;
                this.Width = BarWidthV;
                this.Height = BarHeightV;
                this.Left = wa.right - BarWidthV - 4;
                this.Top = wa.top + (wa.height - BarHeightV) / 2;
            }
            else
            {
                CompanionBarVertical.Visibility = Visibility.Collapsed;
                CompanionBarHorizontal.Visibility = Visibility.Visible;
                this.Width = BarWidthH;
                this.Height = BarHeightH;
                this.Left = wa.left + (wa.width - BarWidthH) / 2;
                this.Top = wa.top + 4;
            }

            RevealFromPeek();
            if (!_isPinned) _autoHideTimer.Start();
        }

        private void BtnOpenDashboard_Click(object sender, RoutedEventArgs e)
        {
            ShowDashboardView();
        }

        // Dedicated Tab Button Handlers
        public void TabNavOverview_Click(object sender, RoutedEventArgs e) => SelectTab("Overview");
        public void TabNavOptimize_Click(object sender, RoutedEventArgs e) => SelectTab("Optimize");
        public void TabNavDiagnostic_Click(object sender, RoutedEventArgs e) => SelectTab("Diagnostic");
        public void TabNavProcesses_Click(object sender, RoutedEventArgs e) => SelectTab("Processes");
        public void BtnGoToProcesses_Click(object sender, RoutedEventArgs e) => SelectTab("Processes");
        public void TabNavSettings_Click(object sender, RoutedEventArgs e) => SelectTab("Settings");
        public void TabNavFeedback_Click(object sender, RoutedEventArgs e) => SelectTab("Feedback");

        public void SelectTab(string tabName)
        {
            // 1. Instantly toggle visibility (0ms UI latency)
            PanelOverview.Visibility = Visibility.Collapsed;
            PanelOptimize.Visibility = Visibility.Collapsed;
            PanelDiagnostic.Visibility = Visibility.Collapsed;
            PanelProcesses.Visibility = Visibility.Collapsed;
            PanelSettings.Visibility = Visibility.Collapsed;
            PanelFeedback.Visibility = Visibility.Collapsed;

            // 2. Reset Indicators and colors
            IndOverview.Visibility = Visibility.Collapsed;
            IndOptimize.Visibility = Visibility.Collapsed;
            IndDiagnostic.Visibility = Visibility.Collapsed;
            IndProcesses.Visibility = Visibility.Collapsed;
            IndSettings.Visibility = Visibility.Collapsed;
            IndFeedback.Visibility = Visibility.Collapsed;

            var muted = (SolidColorBrush)FindResource("TextSecondary");
            var accent = (SolidColorBrush)FindResource("PrimaryAccent");

            IconNavOverview.Fill = muted; TxtNavOverview.Foreground = muted;
            IconNavOptimize.Fill = muted; TxtNavOptimize.Foreground = muted;
            IconNavDiagnostic.Fill = muted; TxtNavDiagnostic.Foreground = muted;
            IconNavProcesses.Fill = muted; TxtNavProcesses.Foreground = muted;
            IconNavSettings.Fill = muted; TxtNavSettings.Foreground = muted;
            IconNavFeedback.Fill = muted; TxtNavFeedback.Foreground = muted;

            _processesRefreshTimer?.Stop();

            switch (tabName)
            {
                case "Overview":
                case "Home":
                    PanelOverview.Visibility = Visibility.Visible;
                    IndOverview.Visibility = Visibility.Visible;
                    IconNavOverview.Fill = accent; TxtNavOverview.Foreground = accent;
                    break;

                case "Optimize":
                case "Cleanup":
                    PanelOptimize.Visibility = Visibility.Visible;
                    IndOptimize.Visibility = Visibility.Visible;
                    IconNavOptimize.Fill = accent; TxtNavOptimize.Foreground = accent;
                    LoadRecycleBinInfo();
                    break;

                case "Diagnostic":
                case "Performance":
                    PanelDiagnostic.Visibility = Visibility.Visible;
                    IndDiagnostic.Visibility = Visibility.Visible;
                    IconNavDiagnostic.Fill = accent; TxtNavDiagnostic.Foreground = accent;
                    LoadStorageDrivesFast();
                    break;

                case "Processes":
                case "Apps":
                    PanelProcesses.Visibility = Visibility.Visible;
                    IndProcesses.Visibility = Visibility.Visible;
                    IconNavProcesses.Fill = accent; TxtNavProcesses.Foreground = accent;
                    LoadRunningProcessesFast();
                    _processesRefreshTimer?.Start();
                    break;

                case "Settings":
                    PanelSettings.Visibility = Visibility.Visible;
                    IndSettings.Visibility = Visibility.Visible;
                    IconNavSettings.Fill = accent; TxtNavSettings.Foreground = accent;
                    LoadStartupApps();
                    break;

                case "Feedback":
                    PanelFeedback.Visibility = Visibility.Visible;
                    IndFeedback.Visibility = Visibility.Visible;
                    IconNavFeedback.Fill = accent; TxtNavFeedback.Foreground = accent;
                    break;
            }
        }

        #endregion

        #region Background Telemetry

        private void FetchTelemetryAsync()
        {
            if (_isFetching) return;
            _isFetching = true;

            Task.Run(() =>
            {
                try
                {
                    var (cpuPercent, cpuTemp, cpuClock) = _hardwareMonitor.GetCpuMetrics();
                    var (totalRam, usedRam, freeRam, ramPercent) = _hardwareMonitor.GetRamMetrics();
                    var (gpuName, gpuTemp, gpuLoad, vramUsed, vramTotal) = _hardwareMonitor.GetGpuMetrics();
                    var (downKb, upKb, downStr, upStr) = _hardwareMonitor.GetNetworkMetrics();
                    var topProcs = (_telemetryTickCount++ % 3 == 0) ? _hardwareMonitor.GetTop5RamProcesses() : Array.Empty<(string, double)>();

                    Dispatcher.InvokeAsync(() =>
                    {
                        UpdateTelemetryUI(cpuPercent, cpuClock, cpuTemp, totalRam, usedRam, freeRam, ramPercent, gpuName, gpuTemp, downStr, upStr, topProcs);
                        _isFetching = false;
                    }, DispatcherPriority.Render);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    LogError("TelemetryFetch", ex);
                    _isFetching = false;
                }
            });
        }

        private void UpdateTelemetryUI(
            float cpuPercent, float cpuClock, float? cpuTemp,
            double totalRam, double usedRam, double freeRam, double ramPercent,
            string gpuName, float? gpuTemp,
            string downStr, string upStr,
            (string processName, double ramMb)[] topProcs)
        {
            // Status-based dynamic coloring: Green < 60%, Amber 60-85%, Red > 85%
            SolidColorBrush GetLoadBrush(double percent)
            {
                if (percent < 60) return (SolidColorBrush)FindResource("TealHealth");
                if (percent <= 85) return (SolidColorBrush)FindResource("AmberWarn");
                return (SolidColorBrush)FindResource("RedAlert");
            }

            var cpuBrush = GetLoadBrush(cpuPercent);
            var ramBrush = GetLoadBrush(ramPercent);

            // Overview Tab: CPU with Threshold Warnings (>85% Alert, 65-85% Warning)
            if (TxtCpuPercent != null)
            {
                TxtCpuPercent.Text = LocalizationManager.FormatPercent(cpuPercent);
                TxtCpuPercent.Foreground = cpuBrush;
            }
            if (CardCpuBorder != null)
            {
                if (cpuPercent >= 85)
                {
                    CardCpuBorder.BorderBrush = (Brush)FindResource("RedAlert");
                    CardCpuBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#291517"));
                }
                else if (cpuPercent >= 65)
                {
                    CardCpuBorder.BorderBrush = (Brush)FindResource("AmberWarn");
                    CardCpuBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#261D15"));
                }
                else
                {
                    CardCpuBorder.BorderBrush = (Brush)FindResource("BorderMuted");
                    CardCpuBorder.Background = (Brush)FindResource("SurfaceContainer");
                }
            }
            if (ProgCpuPercent != null)
            {
                ProgCpuPercent.Value = cpuPercent;
                ProgCpuPercent.Foreground = cpuBrush;
            }
            if (TxtCpuClockTemp != null)
            {
                TxtCpuClockTemp.Text = cpuTemp.HasValue ? $"{cpuTemp.Value:F0}°C | {cpuClock:F2} GHz" : $"{cpuClock:F2} GHz";
            }

            // Overview Tab: RAM with Threshold Warnings (>85% Alert, 65-85% Warning)
            if (TxtRamPercentValue != null)
            {
                TxtRamPercentValue.Text = LocalizationManager.FormatPercent(ramPercent);
                TxtRamPercentValue.Foreground = ramBrush;
            }
            if (CardRamBorder != null)
            {
                if (ramPercent >= 85)
                {
                    CardRamBorder.BorderBrush = (Brush)FindResource("RedAlert");
                    CardRamBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#291517"));
                }
                else if (ramPercent >= 65)
                {
                    CardRamBorder.BorderBrush = (Brush)FindResource("AmberWarn");
                    CardRamBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#261D15"));
                }
                else
                {
                    CardRamBorder.BorderBrush = (Brush)FindResource("BorderMuted");
                    CardRamBorder.Background = (Brush)FindResource("SurfaceContainer");
                }
            }
            if (ProgRamPercent != null)
            {
                ProgRamPercent.Value = ramPercent;
                ProgRamPercent.Foreground = ramBrush;
            }
            if (TxtRamUsageFormatted != null)
            {
                TxtRamUsageFormatted.Text = LocalizationManager.FormatRamSummary(usedRam, totalRam);
            }

            // Diagnostic Tab
            PerfGpuInfo.Text = gpuTemp.HasValue ? $"{gpuName} ({gpuTemp.Value:F0}°C)" : gpuName;
            PerfNetDown.Text = $"↓ {downStr.Replace(" ", "")}";
            PerfNetUp.Text = $"↑ {upStr.Replace(" ", "")}";

            if (topProcs.Length > 0 && ListTopProcesses != null)
            {
                ListTopProcesses.ItemsSource = topProcs.Select(p => new ProcessViewModel { ProcessName = p.processName, RamMb = p.ramMb }).ToList();
            }

            // Companion Bar Telemetry (Horizontal & Vertical)
            if (BarRamTextH != null) { BarRamTextH.Text = $"{ramPercent:F0}%"; BarRamTextH.Foreground = ramBrush; }
            if (BarRamIconH != null) BarRamIconH.Fill = ramBrush;
            if (BarCpuTextH != null) { BarCpuTextH.Text = $"{cpuPercent:F0}%"; BarCpuTextH.Foreground = cpuBrush; }
            if (BarCpuIconH != null) BarCpuIconH.Fill = cpuBrush;
            
            string netDownShort = downStr.Contains("MB") ? $"{downStr.Split(' ')[0]}M" : $"{downStr.Split(' ')[0]}K";
            string netUpShort = upStr.Contains("MB") ? $"{upStr.Split(' ')[0]}M" : $"{upStr.Split(' ')[0]}K";

            if (BarNetDownH != null) BarNetDownH.Text = netDownShort;
            if (BarNetUpH != null) BarNetUpH.Text = netUpShort;

            if (BarRamTextV != null) { BarRamTextV.Text = $"{ramPercent:F0}%"; BarRamTextV.Foreground = ramBrush; }
            if (BarRamIconV != null) BarRamIconV.Fill = ramBrush;
            if (BarCpuTextV != null) { BarCpuTextV.Text = $"{cpuPercent:F0}%"; BarCpuTextV.Foreground = cpuBrush; }
            if (BarCpuIconV != null) BarCpuIconV.Fill = cpuBrush;
            if (BarNetSpeedV != null) BarNetSpeedV.Text = netDownShort;

            // Temperature Pods in Toolbar (Horizontal & Vertical)
            if (ToolbarTempBorderH != null)
            {
                if (cpuTemp.HasValue && cpuTemp.Value > 0)
                {
                    ToolbarTempBorderH.Visibility = Visibility.Visible;
                    var tempBrush = cpuTemp.Value >= 85 ? (SolidColorBrush)FindResource("RedAlert") :
                                    cpuTemp.Value >= 70 ? (SolidColorBrush)FindResource("AmberWarn") :
                                    (SolidColorBrush)FindResource("TealHealth");
                    if (BarTempTextH != null)
                    {
                        BarTempTextH.Text = $"{cpuTemp.Value:F0}°C";
                        BarTempTextH.Foreground = tempBrush;
                    }
                    if (BarTempIconH != null) BarTempIconH.Fill = tempBrush;
                }
                else
                {
                    ToolbarTempBorderH.Visibility = Visibility.Collapsed;
                }
            }

            if (ToolbarTempBorderV != null)
            {
                if (cpuTemp.HasValue && cpuTemp.Value > 0)
                {
                    ToolbarTempBorderV.Visibility = Visibility.Visible;
                    var tempBrush = cpuTemp.Value >= 85 ? (SolidColorBrush)FindResource("RedAlert") :
                                    cpuTemp.Value >= 70 ? (SolidColorBrush)FindResource("AmberWarn") :
                                    (SolidColorBrush)FindResource("TealHealth");
                    if (BarTempTextV != null)
                    {
                        BarTempTextV.Text = $"{cpuTemp.Value:F0}°";
                        BarTempTextV.Foreground = tempBrush;
                    }
                    if (BarTempIconV != null) BarTempIconV.Fill = tempBrush;
                }
                else
                {
                    ToolbarTempBorderV.Visibility = Visibility.Collapsed;
                }
            }

            // Real Resource Status calculation
            int loadPercent = (int)(ramPercent * 0.5 + cpuPercent * 0.5);
            if (loadPercent < 0) loadPercent = 0;
            if (loadPercent > 100) loadPercent = 100;

            TxtHealthPercent.Text = $"{loadPercent}%";

            if (loadPercent <= 50)
            {
                TxtHealthStatus.Text = (LocalizationManager.CurrentLanguage == "ar")
                    ? "استهلاك الموارد: منخفض ومستقر"
                    : "Resource Usage: Low & Optimal";
                DotHealthGlow.Fill = (SolidColorBrush)FindResource("TealHealth");
            }
            else if (loadPercent <= 80)
            {
                TxtHealthStatus.Text = (LocalizationManager.CurrentLanguage == "ar")
                    ? "استهلاك الموارد: متوسط"
                    : "Resource Usage: Moderate";
                DotHealthGlow.Fill = (SolidColorBrush)FindResource("AmberWarn");
            }
            else
            {
                TxtHealthStatus.Text = (LocalizationManager.CurrentLanguage == "ar")
                    ? "استهلاك الموارد: مرتفع"
                    : "Resource Usage: High Load";
                DotHealthGlow.Fill = (SolidColorBrush)FindResource("RedAlert");
            }
        }

        #endregion

        #region Real Features: Fast Storage, Recycle Bin, Startup, Tools

        private void LoadRecycleBinInfo()
        {
            Task.Run(() =>
            {
                var info = _cleanupService.QueryRecycleBin();
                Dispatcher.InvokeAsync(() =>
                {
                    TxtRecycleBinDetails.Text = (LocalizationManager.CurrentLanguage == "ar") ? $"{info.ItemCount} عنصر (\u200E{info.TotalSizeMb:F1} MB\u200E)" : $"{info.ItemCount} items (\u200E{info.TotalSizeMb:F1} MB\u200E)";
                    TxtRecRecycleBinSize.Text = $"{info.TotalSizeMb:F1} MB";
                });
            });
        }

        private void LoadStartupApps()
        {
            Task.Run(() =>
            {
                var apps = _hardwareMonitor.GetStartupApps();
                var boot = _hardwareMonitor.GetBootPerformanceInfo(apps);

                Dispatcher.InvokeAsync(() =>
                {
                    var securityApps = apps.Where(a => a.IsSecurityApp)
                        .OrderByDescending(a => a.IsEnabled)
                        .ThenByDescending(a => a.Impact)
                        .ThenBy(a => a.DisplayName)
                        .ToList();

                    var regularApps = apps.Where(a => !a.IsSecurityApp)
                        .OrderByDescending(a => a.IsEnabled)
                        .ThenByDescending(a => a.Impact)
                        .ThenBy(a => a.DisplayName)
                        .ToList();

                    if (ListStartupSecurityApps != null) ListStartupSecurityApps.ItemsSource = securityApps;
                    if (ListStartupRegularApps != null) ListStartupRegularApps.ItemsSource = regularApps;

                    int enabledCount = apps.Count(a => a.IsEnabled);
                    string countFormat = LocalizationManager.GetString(
                        "StartupActiveCountFormat",
                        LocalizationManager.CurrentLanguage == "ar" ? "{0} مفعّل" : "{0} Active");
                    TxtRecStartupCount.Text = countFormat.Contains("{1")
                        ? string.Format(countFormat, enabledCount, boot.ActiveAppsDelaySeconds ?? 0)
                        : string.Format(countFormat, enabledCount);

                    if (TxtSecurityAppsCount != null) TxtSecurityAppsCount.Text = $"{securityApps.Count(a => a.IsEnabled)}/{securityApps.Count}";
                    if (TxtRegularAppsCount != null) TxtRegularAppsCount.Text = $"{regularApps.Count(a => a.IsEnabled)}/{regularApps.Count}";

                    // Populate Boot Diagnostics Pod
                    if (TxtBiosTimeVal != null) TxtBiosTimeVal.Text = boot.BiosTimeSeconds.HasValue ? $"{boot.BiosTimeSeconds.Value:F1}s" : "--";
                    if (TxtTotalBootVal != null) TxtTotalBootVal.Text = boot.MainPathBootSeconds.HasValue ? $"{boot.MainPathBootSeconds.Value:F1}s" : "--";
                    if (TxtAppsDelayVal != null) TxtAppsDelayVal.Text = (boot.IsMeasured && boot.ActiveAppsDelaySeconds.HasValue) ? $"+{boot.ActiveAppsDelaySeconds.Value:F1}s" : "--";
                    if (TxtBootRating != null) TxtBootRating.Text = boot.IsMeasured && !string.IsNullOrWhiteSpace(boot.RatingText) ? boot.RatingText : LocalizationManager.GetString("BootNA", LocalizationManager.CurrentLanguage == "ar" ? "غير متوفر" : "N/A");
                    if (TxtBootTip != null) TxtBootTip.Text = boot.Recommendation;

                    // Initialize Start With Windows checkbox
                    if (ChkStartWithWindows != null)
                    {
                        ChkStartWithWindows.IsChecked = HardwareMonitorService.IsRunAtStartupEnabled();
                    }
                });
            });
        }

        private void LoadStorageDrivesFast()
        {
            Task.Run(() =>
            {
                var drives = _hardwareMonitor.GetStorageMetricsWithDriveType();
                Dispatcher.InvokeAsync(() =>
                {
                    ListStorageDrives.ItemsSource = drives.Select(d => new StorageDriveViewModel
                    {
                        DriveLetter = d.DriveLetter,
                        VolumeLabel = d.VolumeLabel,
                        TotalGb = d.TotalGb,
                        FreeGb = d.FreeGb,
                        UsedPercent = d.UsedPercent,
                        MediaType = d.MediaType
                    }).ToList();

                    var cDrive = drives.FirstOrDefault(d => d.DriveLetter.StartsWith("C", StringComparison.OrdinalIgnoreCase));
                    if (cDrive != null)
                    {
                        TxtStorageFree.Text = (LocalizationManager.CurrentLanguage == "ar")
                            ? $"\u200E{cDrive.FreeGb:F1} GB\u200E متاح"
                            : $"\u200E{cDrive.FreeGb:F1} GB\u200E Free";
                        ProgStorageUsed.Value = cDrive.UsedPercent;

                        // Companion Bar Storage Pod
                        var diskBrush = cDrive.UsedPercent >= 90 ? (SolidColorBrush)FindResource("RedAlert") :
                                        cDrive.UsedPercent >= 80 ? (SolidColorBrush)FindResource("AmberWarn") :
                                        (SolidColorBrush)FindResource("TealHealth");
                        if (BarDiskTextH != null)
                        {
                            BarDiskTextH.Text = $"{cDrive.UsedPercent:F0}%";
                            BarDiskTextH.Foreground = diskBrush;
                        }
                        if (BarDiskIconH != null) BarDiskIconH.Fill = diskBrush;
                        if (BarDiskTextV != null)
                        {
                            BarDiskTextV.Text = $"{cDrive.UsedPercent:F0}%";
                            BarDiskTextV.Foreground = diskBrush;
                        }
                        if (BarDiskIconV != null) BarDiskIconV.Fill = diskBrush;
                    }
                });
            });
        }

        private void LaunchTool_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string toolKey)
            {
                string cmd = toolKey switch
                {
                    "taskmgr" => "taskmgr.exe",
                    "diskmgmt" => "diskmgmt.msc",
                    "resmon" => "resmon.exe",
                    "services" => "services.msc",
                    "devmgmt" => "devmgmt.msc",
                    "ncpa" => "ncpa.cpl",
                    _ => ""
                };

                bool isAr = (LocalizationManager.CurrentLanguage == "ar");
                string toolName = toolKey switch
                {
                    "taskmgr" => isAr ? "مدير المهام" : "Task Manager",
                    "diskmgmt" => isAr ? "إدارة الأقراص" : "Disk Management",
                    "resmon" => isAr ? "مراقب الموارد" : "Resource Monitor",
                    "services" => isAr ? "خدمات ويندوز" : "Services",
                    "devmgmt" => isAr ? "إدارة الأجهزة" : "Device Manager",
                    "ncpa" => isAr ? "محولات الشبكة" : "Network Connections",
                    _ => isAr ? "الأداة" : "Tool"
                };

                if (!string.IsNullOrEmpty(cmd))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo { FileName = cmd, UseShellExecute = true });
                        ShowToast(isAr ? $"تم فتح {toolName}" : $"{toolName} opened", false);
                    }
                    catch (Exception ex)
                    {
                        ShowToast(isAr ? $"تعذر الفتح: {ex.Message}" : $"Could not open: {ex.Message}", true);
                    }
                }
            }
        }

        private void BtnOpenStartupSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                LogError("OpenStartupSettingsFallback", ex);
                try { Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true }); }
                catch (Exception ex2) when (ex2 is Win32Exception or InvalidOperationException) { ShowToast((LocalizationManager.CurrentLanguage == "ar") ? $"تعذر فتح إعدادات بدء التشغيل: {ex2.Message}" : $"Unable to open Startup Settings: {ex2.Message}", true); }
            }
        }

        private void ChkStartWithWindows_Click(object sender, RoutedEventArgs e)
        {
            if (ChkStartWithWindows == null) return;
            bool enable = ChkStartWithWindows.IsChecked == true;
            bool ok = HardwareMonitorService.SetRunAtStartup(enable);
            if (ok)
            {
                ShowToast(LocalizationManager.CurrentLanguage == "ar"
                    ? (enable ? "تم تفعيل بدء تشغيل Tempo مع ويندوز" : "تم إلغاء بدء تشغيل Tempo مع ويندوز")
                    : (enable ? "Tempo will now start with Windows" : "Tempo startup with Windows disabled"), false);
            }
            else
            {
                ChkStartWithWindows.IsChecked = !enable;
                ShowToast(LocalizationManager.CurrentLanguage == "ar" ? "تعذر تغيير إعداد بدء التشغيل" : "Failed to update startup setting", true);
            }
        }

        private async void BtnMeasureBoot_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null) btn.IsEnabled = false;

            bool isAr = LocalizationManager.CurrentLanguage == "ar";
            ShowToast(isAr ? "جاري قياس أثر الإقلاع الفعلي..." : "Measuring real boot impact...", false);

            try
            {
                var result = await Task.Run(() => _hardwareMonitor.MeasureBootPerformanceElevated());
                if (result == BootMeasureResult.UserCancelledUac)
                {
                    ShowToast(LocalizationManager.GetString("BootDeniedUAC", isAr ? "تم إلغاء القياس (لم يتم منح صلاحيات المسؤول)" : "Measurement cancelled (Administrator permission was not granted)"), true);
                }
                else if (result == BootMeasureResult.Success)
                {
                    ShowToast(isAr ? "تم قياس أثر الإقلاع وتحديث البيانات بنجاح" : "Boot impact measured and updated successfully", false);
                    LoadStartupApps();
                }
                else
                {
                    ShowToast(isAr ? "تعذر قياس أثر الإقلاع من سجلات النظام" : "Failed to measure boot impact from system logs", true);
                }
            }
            finally
            {
                if (btn != null) btn.IsEnabled = true;
            }
        }

        private async void BtnDisableStartupApp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is StartupAppItem app)
            {
                bool isAr = (LocalizationManager.CurrentLanguage == "ar");

                if (app.IsSystemManaged)
                {
                    ShowToast(isAr
                        ? "خدمة أمان محمية: يديرها نظام ويندوز مباشرة ولا يمكن إيقافها."
                        : "Protected system service: Managed directly by Windows OS and cannot be stopped.", false);
                    return;
                }

                // If currently disabled, re-enable it directly
                if (!app.IsEnabled)
                {
                    btn.IsEnabled = false;
                    bool okEnable = await Task.Run(() => _hardwareMonitor.ToggleStartupApp(app));
                    btn.IsEnabled = true;

                    if (okEnable)
                    {
                        ShowToast(isAr ? $"تم تفعيل {app.DisplayName}" : $"{app.DisplayName} enabled", false);
                        LoadStartupApps();
                    }
                    else
                    {
                        ShowToast(isAr ? $"تعذر تفعيل {app.DisplayName}. قد يتطلب صلاحيات مسؤول." : $"Unable to enable {app.DisplayName}. Administrator privileges may be required.", true);
                    }
                    return;
                }

                // Check for Security / System sensitive applications
                bool isSecurityApp = app.IsSecurityApp;

                string securityNotice = isSecurityApp
                    ? (isAr ? "\n\n⚠️ تحذير أمني شديد: هذا البرنامج يبدو مرتبطاً بالحماية أو مكافحة الفيروسات! تعطيله قد يؤثر على أمان النظام."
                            : "\n\n⚠️ High Security Warning: This application appears to be security/antivirus related! Disabling it may compromise system safety.")
                    : "";

                string title = isAr ? "إيقاف برنامج بدء التشغيل" : "Disable Startup App";
                string msg = isAr
                    ? $"هل تريد إيقاف تشغيل {app.DisplayName} تلقائياً عند فتح الجهاز؟" + securityNotice
                    : $"Stop {app.DisplayName} from starting automatically with Windows?" + securityNotice;

                var confirm = MessageBox.Show(
                    msg,
                    title,
                    MessageBoxButton.YesNo,
                    isSecurityApp ? MessageBoxImage.Warning : MessageBoxImage.Question,
                    MessageBoxResult.No);

                if (confirm != MessageBoxResult.Yes) return;

                btn.IsEnabled = false;
                bool ok = await Task.Run(() => _hardwareMonitor.ToggleStartupApp(app));
                btn.IsEnabled = true;

                if (ok)
                {
                    ShowToast(isAr ? $"تم تعطيل {app.DisplayName}" : $"{app.DisplayName} disabled", false);
                    LoadStartupApps();
                }
                else
                {
                    ShowToast(isAr ? $"تعذر تغيير حالة {app.DisplayName}. قد يتطلب صلاحيات مسؤول." : $"Unable to update {app.DisplayName}. Administrator privileges may be required.", true);
                }
            }
        }

        #endregion

        #region Cleanup Handlers & Recommendations

        public void BtnHeroOptimize_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null) btn.IsEnabled = false;
            ShowToast((LocalizationManager.CurrentLanguage == "ar") ? "جاري تنظيف وتسريع الجهاز..." : "Cleaning PC & boosting RAM...", false);

            Task.Run(async () =>
            {
                var ramRes = _cleanupService.TurboRamBoost();
                var tempRes = _cleanupService.QuickCleanTemp();

                // Allow 350ms for Windows Memory Manager and filesystem to stabilize
                await Task.Delay(350);

                await Dispatcher.InvokeAsync(() =>
                {
                    if (btn != null) btn.IsEnabled = true;
                    ShowToast((LocalizationManager.CurrentLanguage == "ar") 
                        ? $"تم التسريع: تحرير \u200E{ramRes.ReclaimedMb:F1} MB\u200E رام و \u200E{tempRes.ReclaimedMb:F1} MB\u200E مؤقت" 
                        : $"Boosted: \u200E{ramRes.ReclaimedMb:F1} MB\u200E RAM & \u200E{tempRes.ReclaimedMb:F1} MB\u200E temp freed", false);

                    FetchTelemetryAsync();
                    LoadStorageDrivesFast();
                    LoadRecycleBinInfo();
                    TxtRecTempSize.Text = "0 MB";
                });
            });
        }

        private void BtnApplyTempClean_Click(object sender, RoutedEventArgs e)
        {
            BtnQuickClean_Click(sender, e);
        }

        private void BtnApplyStartup_Click(object sender, RoutedEventArgs e)
        {
            SelectTab("Settings");
        }

        private void BtnApplyRecycleBin_Click(object sender, RoutedEventArgs e)
        {
            BtnEmptyRecycleBin_Click(sender, e);
        }

        private void BtnScanAll_Click(object sender, RoutedEventArgs e)
        {
            BtnScanAll.IsEnabled = false;
            TxtScanSummary.Text = (LocalizationManager.CurrentLanguage == "ar") ? "جاري فحص الجهاز..." : "Scanning PC...";

            Task.Run(() =>
            {
                var summary = _cleanupService.ScanAllCaches();
                Dispatcher.InvokeAsync(() =>
                {
                    BtnScanAll.IsEnabled = true;
                    TxtTempDetails.Text = (LocalizationManager.CurrentLanguage == "ar") ? $"{summary.TempFiles} ملف مؤقت (\u200E{summary.TempMb:F1} MB\u200E)" : $"{summary.TempFiles} temp files (\u200E{summary.TempMb:F1} MB\u200E)";
                    TxtRecycleBinDetails.Text = (LocalizationManager.CurrentLanguage == "ar") ? $"{summary.RecycleBinItems} عنصر (\u200E{summary.RecycleBinMb:F1} MB\u200E)" : $"{summary.RecycleBinItems} items (\u200E{summary.RecycleBinMb:F1} MB\u200E)";
                    TxtBrowserDetails.Text = (LocalizationManager.CurrentLanguage == "ar") ? $"Chrome & Edge ({summary.BrowserCacheFiles} ملف، \u200E{summary.BrowserCacheMb:F1} MB\u200E)" : $"Chrome & Edge ({summary.BrowserCacheFiles} files, \u200E{summary.BrowserCacheMb:F1} MB\u200E)";
                    TxtDevDetails.Text = (LocalizationManager.CurrentLanguage == "ar") ? $"npm, NuGet, pip ({summary.DevCacheFiles} ملف، \u200E{summary.DevCacheMb:F1} MB\u200E)" : $"npm, NuGet, pip ({summary.DevCacheFiles} files, \u200E{summary.DevCacheMb:F1} MB\u200E)";
                    TxtScanSummary.Text = (LocalizationManager.CurrentLanguage == "ar") ? $"المخلفات: \u200E{summary.TotalMb:F1} MB\u200E ({summary.TotalItems} ملف)" : $"Found: \u200E{summary.TotalMb:F1} MB\u200E ({summary.TotalItems} files)";
                    TxtTargetFilesSize.Text = $"{summary.TotalMb:F1} MB";
                    TxtRecTempSize.Text = $"{summary.TempMb:F1} MB";
                    TxtRecRecycleBinSize.Text = $"{summary.RecycleBinMb:F1} MB";
                    ShowToast((LocalizationManager.CurrentLanguage == "ar") ? $"اكتمل الفحص: \u200E{summary.TotalMb:F1} MB\u200E جاهزة للتنظيف" : $"Scan complete: \u200E{summary.TotalMb:F1} MB\u200E to clean", false);
                });
            });
        }

        private async void BtnQuickClean_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null) btn.IsEnabled = false;
            try
            {
                var res = await Task.Run(() => _cleanupService.QuickCleanTemp());
                ShowToast(res.Message, false);
                LoadRecycleBinInfo();
                if (TxtRecTempSize != null) TxtRecTempSize.Text = "0 MB";
                if (TxtTempDetails != null)
                {
                    TxtTempDetails.Text = LocalizationManager.GetString(
                        "TxtTempDetailsCleaned",
                        LocalizationManager.CurrentLanguage == "ar" ? "تم تنظيف الملفات المؤقتة" : "Temporary files cleaned");
                }
            }
            finally
            {
                if (btn != null) btn.IsEnabled = true;
            }
        }

        private async void BtnEmptyRecycleBin_Click(object sender, RoutedEventArgs e)
        {
            var before = await Task.Run(() => _cleanupService.QueryRecycleBin());
            if (before.ItemCount == 0)
            {
                ShowToast((LocalizationManager.CurrentLanguage == "ar") ? "سلة المحذوفات فارغة بالفعل (0 عنصر)." : "Recycle Bin is already empty (0 items).", false);
                return;
            }

            string title = (LocalizationManager.CurrentLanguage == "ar") ? "تفريغ سلة المحذوفات" : "Empty Recycle Bin";
            string msg = (LocalizationManager.CurrentLanguage == "ar")
                ? $"هل تريد تفريغ سلة المحذوفات نهائياً؟\n({before.ItemCount:N0} عنصر — {LocalizationManager.FormatMb(before.TotalSizeMb)})"
                : $"Permanently empty the Recycle Bin?\n({before.ItemCount:N0} items — {LocalizationManager.FormatMb(before.TotalSizeMb)})";

            var confirm = MessageBox.Show(
                msg,
                title,
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning,
                MessageBoxResult.Cancel);

            if (confirm != MessageBoxResult.OK) return;

            var btn = sender as Button;
            if (btn != null) btn.IsEnabled = false;
            try
            {
                var res = await Task.Run(() => _cleanupService.EmptyRecycleBin());
                ShowToast(res.Message, !res.Success);
                LoadRecycleBinInfo();
            }
            finally
            {
                if (btn != null) btn.IsEnabled = true;
            }
        }

        private async void BtnBrowserClean_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null) btn.IsEnabled = false;
            try
            {
                var res = await Task.Run(() => _cleanupService.BrowserCacheFlush());
                ShowToast(res.Message, !res.Success);
                if (TxtBrowserDetails != null)
                {
                    TxtBrowserDetails.Text = LocalizationManager.GetString(
                        "TxtBrowserDetailsCleaned",
                        LocalizationManager.CurrentLanguage == "ar" ? "تم تنظيف كاش المتصفح" : "Browser cache cleaned");
                }
            }
            finally
            {
                if (btn != null) btn.IsEnabled = true;
            }
        }

        private async void BtnDevClean_Click(object sender, RoutedEventArgs e)
        {
            string title = (LocalizationManager.CurrentLanguage == "ar") ? "تنظيف كاش المطورين" : "Clean Dev Cache";
            string msg = (LocalizationManager.CurrentLanguage == "ar")
                ? "هل تريد حذف كاش حزم npm و NuGet و pip المؤقتة؟"
                : "Clean temporary package caches for npm, NuGet, and pip?";

            var confirm = MessageBox.Show(
                msg,
                title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm == MessageBoxResult.Yes)
            {
                var btn = sender as Button;
                if (btn != null) btn.IsEnabled = false;
                try
                {
                    var res = await Task.Run(() => _cleanupService.DevCachesFlush());
                    ShowToast(res.Message, false);
                    if (TxtDevDetails != null)
                    {
                        TxtDevDetails.Text = LocalizationManager.GetString(
                            "TxtDevDetailsCleaned",
                            LocalizationManager.CurrentLanguage == "ar" ? "تم تنظيف كاش المطورين" : "Developer cache cleaned");
                    }
                }
                finally
                {
                    if (btn != null) btn.IsEnabled = true;
                }
            }
        }

        private void BtnSsdTrim_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is StorageDriveViewModel vm)
            {
                if (vm.IsUnknown)
                {
                    bool isAr = (LocalizationManager.CurrentLanguage == "ar");
                    ShowToast(isAr ? "نوع القرص غير معروف — تم تعطيل TRIM" : "Unknown drive type — TRIM disabled", true);
                    return;
                }

                string letter = vm.DriveLetter.TrimEnd(':', '\\', '/');
                bool isSsd = vm.MediaType.Equals("SSD", StringComparison.OrdinalIgnoreCase);

                bool isArMsg = (LocalizationManager.CurrentLanguage == "ar");
                string actionMsg = isSsd
                    ? (isArMsg ? $"جاري تنشيط خلايا SSD عبر TRIM للقرص {letter}:... يرجى الانتظار." : $"Optimizing SSD via TRIM on drive {letter}:... please wait.")
                    : (isArMsg ? $"جاري تحسين وإلغاء تجزئة القرص {letter}:... يرجى الانتظار." : $"Optimizing and defragmenting drive {letter}:... please wait.");

                ShowToast(actionMsg, false);
                btn.IsEnabled = false;

                Task.Run(() =>
                {
                    var res = _cleanupService.SsdReTrim(letter);
                    Dispatcher.InvokeAsync(() =>
                    {
                        btn.IsEnabled = vm.IsTrimEnabled;
                        ShowToast(res.Message, !res.Success);
                    });
                });
            }
        }

        private DispatcherTimer? _toastTimer;

        private void ShowToast(string message, bool isWarning = false)
        {
            try
            {
                // Never show toast popups or hide metrics when in Toolbar companion mode
                if (_currentView == AppViewMode.Toolbar)
                {
                    return;
                }

                var brush = (SolidColorBrush)FindResource(isWarning ? "AmberWarn" : "TealHealth");
                string iconData = isWarning
                    ? "M12,2 C6.48,2 2,6.48 2,12 C2,17.52 6.48,22 12,22 C17.52,22 22,17.52 22,12 C22,6.48 17.52,2 12,2 Z M13,17 L11,17 L11,15 L13,15 L13,17 Z M13,13 L11,13 L11,7 L13,7 L13,13 Z"
                    : "M12,2 C6.48,2 2,6.48 2,12 C2,17.52 6.48,22 12,22 C17.52,22 22,17.52 22,12 C22,6.48 17.52,2 12,2 Z M10,17 L5,12 L6.41,10.59 L10,14.17 L17.59,6.58 L19,8 L10,17 Z";

                _toastTimer?.Stop();

                    ToastText.Text = message;
                    ToastText.Foreground = brush;
                    ToastBanner.BorderBrush = brush;
                    ToastBanner.Background = new SolidColorBrush(Color.FromArgb(248, 20, 24, 32));
                    if (ToastIcon != null)
                    {
                        ToastIcon.Fill = brush;
                        ToastIcon.Data = Geometry.Parse(iconData);
                    }
                    ToastBanner.Visibility = Visibility.Visible;

                    if (_toastTimer == null)
                    {
                        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
                        _toastTimer.Tick += (s, e) =>
                        {
                            _toastTimer.Stop();
                            if (ToastBanner != null) ToastBanner.Visibility = Visibility.Collapsed;
                        };
                    }
                    _toastTimer.Start();
            }
            catch (Exception ex)
            {
                LogError("ShowToast", ex);
            }
        }

        #endregion

        #region Toolbar Settings & Auto-Hide Peek

        public void BtnLaunchToolbar_Click(object sender, RoutedEventArgs e)
        {
            _isToolbarEnabled = true;
            UpdateToolbarSettingsUI();
            SaveSettings();
            ShowToolbarView();
            ShowToast((LocalizationManager.CurrentLanguage == "ar") ? "تم التبديل إلى الشريط المصغر" : "Switched to Mini Bar", false);
        }

        private void BtnToggleToolbar_Click(object sender, RoutedEventArgs e)
        {
            _isToolbarEnabled = !_isToolbarEnabled;
            UpdateToolbarSettingsUI();
            SaveSettings();
            ShowToast(_isToolbarEnabled ? ((LocalizationManager.CurrentLanguage == "ar") ? "تم تفعيل الشريط المصغر" : "Mini Bar enabled") : ((LocalizationManager.CurrentLanguage == "ar") ? "تم إيقاف الشريط المصغر" : "Mini Bar disabled"), false);
        }

        private void UpdateToolbarSettingsUI()
        {
            if (_isToolbarEnabled)
            {
                BtnToggleToolbar.Content = (LocalizationManager.CurrentLanguage == "ar") ? "مفعّل" : "Enabled";
                BtnToggleToolbar.Foreground = (SolidColorBrush)FindResource("TealHealth");
                ToolbarPlacementPanel.Visibility = Visibility.Visible;
            }
            else
            {
                BtnToggleToolbar.Content = (LocalizationManager.CurrentLanguage == "ar") ? "معطّل" : "Disabled";
                BtnToggleToolbar.Foreground = (SolidColorBrush)FindResource("TextMuted");
                ToolbarPlacementPanel.Visibility = Visibility.Collapsed;
            }

            // Update Active highlights for the 2 dock placement buttons
            var activeBorder = (SolidColorBrush)FindResource("PrimaryCobalt");
            var activeBg = new SolidColorBrush(Color.FromArgb(255, 34, 38, 49));
            var normalBorder = (SolidColorBrush)FindResource("BorderMuted");
            var normalBg = new SolidColorBrush(Color.FromArgb(255, 22, 25, 34));

            if (BtnDockTop != null)
            {
                bool isTop = _toolbarDock == DockPosition.TopToolbar;
                BtnDockTop.BorderBrush = isTop ? activeBorder : normalBorder;
                BtnDockTop.Background = isTop ? activeBg : normalBg;
                BtnDockTop.Foreground = isTop ? (SolidColorBrush)FindResource("PrimaryAccent") : (SolidColorBrush)FindResource("TextSecondary");
            }
            if (BtnDockSide != null)
            {
                bool isSide = _toolbarDock == DockPosition.Side;
                BtnDockSide.BorderBrush = isSide ? activeBorder : normalBorder;
                BtnDockSide.Background = isSide ? activeBg : normalBg;
                BtnDockSide.Foreground = isSide ? (SolidColorBrush)FindResource("PrimaryAccent") : (SolidColorBrush)FindResource("TextSecondary");
            }
        }

        private void SetDock_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string mode)
            {
                _toolbarDock = (mode == "Side") ? DockPosition.Side : DockPosition.TopToolbar;

                UpdateToolbarSettingsUI();
                SaveSettings();
                bool isAr = (LocalizationManager.CurrentLanguage == "ar");
                string posName = (_toolbarDock == DockPosition.Side)
                    ? (isAr ? "الجانب" : "Side of Screen")
                    : (isAr ? "أعلى الشاشة" : "Top of Screen");
                ShowToast(isAr ? $"تم ضبط موضع الشريط: {posName}" : $"Toolbar position set to: {posName}", false);

                if (_currentView == AppViewMode.Toolbar)
                {
                    ShowToolbarView();
                }
            }
        }

        private void TriggerToolbarBoostMotions()
        {
            try
            {
                // 1. Lightning Icon Rotation: Fast 720° spin with smooth quartic deceleration
                var rotAnim = new DoubleAnimation(0, 720, TimeSpan.FromMilliseconds(700))
                {
                    EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
                };

                // 2. Button Scale Punch: Elastic bounce from 1.0 -> 1.22 -> 1.0
                var scaleAnim = new DoubleAnimationUsingKeyFrames
                {
                    Duration = TimeSpan.FromMilliseconds(550)
                };
                scaleAnim.KeyFrames.Add(new SplineDoubleKeyFrame(1.22, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160))));
                scaleAnim.KeyFrames.Add(new SplineDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(550)), new KeySpline(0.2, 0.8, 0.4, 1.0)));

                if (ToolbarBoostRotateH != null) ToolbarBoostRotateH.BeginAnimation(RotateTransform.AngleProperty, rotAnim);
                if (ToolbarBoostRotateV != null) ToolbarBoostRotateV.BeginAnimation(RotateTransform.AngleProperty, rotAnim);

                if (ToolbarBoostScaleH != null)
                {
                    ToolbarBoostScaleH.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
                    ToolbarBoostScaleH.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
                }
                if (ToolbarBoostScaleV != null)
                {
                    ToolbarBoostScaleV.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
                    ToolbarBoostScaleV.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
                }

                if (BtnToolbarBoostScaleH != null)
                {
                    BtnToolbarBoostScaleH.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
                    BtnToolbarBoostScaleH.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
                }
                if (BtnToolbarBoostScaleV != null)
                {
                    BtnToolbarBoostScaleV.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
                    BtnToolbarBoostScaleV.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
                }

                // 3. Toolbar Pod Border Ripple: Subtle emerald glow ripple across the border
                var borderAnim = new ColorAnimation
                {
                    From = Color.FromRgb(0x2E, 0x3A, 0x52),
                    To = Color.FromRgb(0x10, 0xB9, 0x81),
                    Duration = TimeSpan.FromMilliseconds(300),
                    AutoReverse = true,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };

                if (CompanionBarHorizontal != null && CompanionBarHorizontal.BorderBrush is SolidColorBrush hBrush)
                {
                    var animatedBrush = hBrush.Clone();
                    CompanionBarHorizontal.BorderBrush = animatedBrush;
                    animatedBrush.BeginAnimation(SolidColorBrush.ColorProperty, borderAnim);
                }
                if (CompanionBarVertical != null && CompanionBarVertical.BorderBrush is SolidColorBrush vBrush)
                {
                    var animatedBrush = vBrush.Clone();
                    CompanionBarVertical.BorderBrush = animatedBrush;
                    animatedBrush.BeginAnimation(SolidColorBrush.ColorProperty, borderAnim);
                }

                // 4. RAM Telemetry Pod: Spin RAM icon and soft shimmer text
                var ramRotAnim = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(600))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                if (BarRamRotateH != null) BarRamRotateH.BeginAnimation(RotateTransform.AngleProperty, ramRotAnim);
                if (BarRamRotateV != null) BarRamRotateV.BeginAnimation(RotateTransform.AngleProperty, ramRotAnim);

                var textFade = new DoubleAnimation(1.0, 0.35, TimeSpan.FromMilliseconds(220))
                {
                    AutoReverse = true,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };
                if (BarRamTextH != null) BarRamTextH.BeginAnimation(OpacityProperty, textFade);
                if (BarRamTextV != null) BarRamTextV.BeginAnimation(OpacityProperty, textFade);

                // 5. Status Dot Pulse
                if (ToolbarPulseScaleH != null)
                {
                    var dotAnim = new DoubleAnimation(1.0, 1.8, TimeSpan.FromMilliseconds(250))
                    {
                        AutoReverse = true,
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                    };
                    ToolbarPulseScaleH.BeginAnimation(ScaleTransform.ScaleXProperty, dotAnim);
                    ToolbarPulseScaleH.BeginAnimation(ScaleTransform.ScaleYProperty, dotAnim);
                }
            }
            catch (Exception ex)
            {
                LogError("TriggerToolbarBoostMotions", ex);
            }
        }

        private void TriggerToolbarCompletionMotions()
        {
            try
            {
                // Soft fade in for live updated metrics
                var completionFade = new DoubleAnimation(0.4, 1.0, TimeSpan.FromMilliseconds(350))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                if (BarRamTextH != null) BarRamTextH.BeginAnimation(OpacityProperty, completionFade);
                if (BarRamTextV != null) BarRamTextV.BeginAnimation(OpacityProperty, completionFade);
                if (BarCpuTextH != null) BarCpuTextH.BeginAnimation(OpacityProperty, completionFade);
                if (BarCpuTextV != null) BarCpuTextV.BeginAnimation(OpacityProperty, completionFade);
            }
            catch (Exception ex)
            {
                LogError("TriggerToolbarCompletionMotions", ex);
            }
        }

        public void BtnToolbarBoost_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null) btn.IsEnabled = false;

            // Trigger smooth, rich in-bar motions (no notification popups/banners)
            TriggerToolbarBoostMotions();

            Task.Run(async () =>
            {
                var ramRes = _cleanupService.TurboRamBoost();
                var tempRes = _cleanupService.QuickCleanTemp();

                // Allow 350ms for Windows Memory Manager and filesystem to stabilize
                await Task.Delay(350);

                await Dispatcher.InvokeAsync(() =>
                {
                    if (btn != null) btn.IsEnabled = true;

                    // Live update telemetry & counters without toast banners
                    FetchTelemetryAsync();
                    LoadStorageDrivesFast();
                    LoadRecycleBinInfo();
                    TxtRecTempSize.Text = "0 MB";

                    // Trigger completion ripple motion
                    TriggerToolbarCompletionMotions();
                });
            });
        }

        public void BtnToolbarPin_Click(object sender, RoutedEventArgs e)
        {
            _isPinned = !_isPinned;
            var pinBrush = _isPinned 
                ? (SolidColorBrush)FindResource("TealHealth") 
                : (SolidColorBrush)FindResource("PrimaryAccent");

            if (ToolbarPinIconH != null) ToolbarPinIconH.Fill = pinBrush;
            if (ToolbarPinIconV != null) ToolbarPinIconV.Fill = pinBrush;

            if (_isPinned)
            {
                _autoHideTimer.Stop();
                RevealFromPeek();
            }
            else
            {
                _autoHideTimer.Start();
            }
        }

        public void BtnToolbarClose_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void CompanionBar_MouseEnter(object sender, MouseEventArgs e)
        {
            _autoHideTimer.Stop();
            if (_isPeeked) RevealFromPeek();
        }

        private void CompanionBar_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_currentView == AppViewMode.Toolbar && !_isPeeked && !_isPinned)
            {
                _autoHideTimer.Start();
            }
        }

        private void AutoHideTimer_Tick(object? sender, EventArgs e)
        {
            _autoHideTimer.Stop();
            if (_currentView == AppViewMode.Toolbar && !_isPeeked && !_isPinned)
            {
                PeekHide();
            }
        }

        public void PeekHide()
        {
            _isPeeked = true;
            var wa = GetCurrentMonitorWorkArea();

            if (_toolbarDock == DockPosition.Side)
            {
                // Slides right leaving 8px peeking
                var leftAnim = new DoubleAnimation(wa.right - 8, TimeSpan.FromMilliseconds(260))
                {
                    EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseInOut }
                };
                var opAnim = new DoubleAnimation(0.75, TimeSpan.FromMilliseconds(260));
                this.BeginAnimation(LeftProperty, leftAnim);
                this.BeginAnimation(OpacityProperty, opAnim);
            }
            else
            {
                // Slides top leaving 8px peeking
                var topAnim = new DoubleAnimation(wa.top - BarHeightH + 8, TimeSpan.FromMilliseconds(260))
                {
                    EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseInOut }
                };
                var opAnim = new DoubleAnimation(0.75, TimeSpan.FromMilliseconds(260));
                this.BeginAnimation(TopProperty, topAnim);
                this.BeginAnimation(OpacityProperty, opAnim);
            }
        }

        public void RevealFromPeek()
        {
            _isPeeked = false;
            var wa = GetCurrentMonitorWorkArea();

            if (_toolbarDock == DockPosition.Side)
            {
                var leftAnim = new DoubleAnimation(wa.right - BarWidthV - 4, TimeSpan.FromMilliseconds(240))
                {
                    EasingFunction = new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut }
                };
                var opAnim = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(180));
                this.BeginAnimation(LeftProperty, leftAnim);
                this.BeginAnimation(OpacityProperty, opAnim);
            }
            else
            {
                var topAnim = new DoubleAnimation(wa.top + 4, TimeSpan.FromMilliseconds(240))
                {
                    EasingFunction = new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut }
                };
                var opAnim = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(180));
                this.BeginAnimation(TopProperty, topAnim);
                this.BeginAnimation(OpacityProperty, opAnim);
            }
        }

        private void CompanionBar_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject dep)
            {
                DependencyObject? current = dep;
                while (current != null && current != CompanionBarHorizontal && current != CompanionBarVertical)
                {
                    if (current is Button) return;
                    current = VisualTreeHelper.GetParent(current);
                }
            }

            if (e.ChangedButton == MouseButton.Left)
            {
                _autoHideTimer.Stop();
                RevealFromPeek();
                this.Cursor = Cursors.SizeAll;

                try { this.DragMove(); } catch (InvalidOperationException) { }

                this.Cursor = Cursors.Arrow;

                var wa = GetCurrentMonitorWorkArea();
                if (this.Top < wa.top + 40)
                {
                    _toolbarDock = DockPosition.TopToolbar;
                    ShowToolbarView();
                }
                else if (this.Left + this.Width > wa.right - 50)
                {
                    _toolbarDock = DockPosition.Side;
                    ShowToolbarView();
                }

                SaveSettings();
                if (!_isPinned) _autoHideTimer.Start();
            }
        }

        #endregion

        #region System Tray & Persistence

        private void UpdateTrayMenu()
        {
            if (_trayIcon == null) return;
            bool isAr = (LocalizationManager.CurrentLanguage == "ar");
            var menu = new TrayContextMenuStrip();

            menu.Items.Add(isAr ? "فتح لوحة التحكم (Tempo)" : "Open Dashboard (Tempo)", null, (s, e) => {
                this.Show();
                this.WindowState = WindowState.Normal;
                ShowDashboardView();
                this.Activate();
            });
            menu.Items.Add(isAr ? "إظهار / إخفاء شريط سطح المكتب" : "Toggle Desktop Companion Toolbar", null, (s, e) => {
                if (_currentView == AppViewMode.Toolbar)
                {
                    ShowDashboardView();
                }
                else
                {
                    ShowToolbarView();
                }
            });
            menu.Items.Add(new TrayToolStripSeparator());
            menu.Items.Add(isAr ? "تحسين الذاكرة الخاملة (Optimize RAM)" : "Optimize RAM Now", null, (s, e) => {
                var res = _cleanupService.TurboRamBoost();
                _cleanupService.QuickCleanTemp();
                ShowToast(res.Message, false);
            });
            menu.Items.Add(new TrayToolStripSeparator());
            menu.Items.Add(isAr ? "إغلاق التطبيق نهائياً" : "Exit Tempo", null, (s, e) => ExitApp());

            _trayIcon.Text = isAr ? "Tempo - تنظيف وتسريع الجهاز" : "Tempo Diagnostic & Optimizer";
            _trayIcon.ContextMenuStrip = menu;
        }

        private void InitSystemTray()
        {
            _trayIcon = new TrayNotifyIcon();

            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
            if (File.Exists(iconPath))
            {
                _trayIcon.Icon = new System.Drawing.Icon(iconPath);
            }
            else
            {
                _trayIcon.Icon = System.Drawing.SystemIcons.Application;
            }

            _trayIcon.Text = (LocalizationManager.CurrentLanguage == "ar") ? "Tempo - تنظيف وتسريع الجهاز" : "Tempo Diagnostic & Optimizer";
            _trayIcon.Visible = true;

            UpdateTrayMenu();
            _trayIcon.MouseClick += (s, e) =>
            {
                if (e.Button == TrayMouseButtons.Left)
                {
                    this.Show();
                    this.WindowState = WindowState.Normal;
                    ShowDashboardView();
                    this.Activate();
                }
            };
        }

        public string GetSettingsFilePath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dir = Path.Combine(appData, "Tempo");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return Path.Combine(dir, "settings.json");
        }

        public void LoadSettings()
        {
            try
            {
                string path = GetSettingsFilePath();
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var s = JsonSerializer.Deserialize<AppSettings>(json);
                    if (s != null)
                    {
                        _isToolbarEnabled = s.IsToolbarEnabled;
                        _toolbarDock = s.ToolbarDock;
                        _selectedLanguage = string.IsNullOrWhiteSpace(s.SelectedLanguage) ? "en" : s.SelectedLanguage;
                        return;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or JsonException)
            {
                LogError("LoadSettings", ex);
            }

            _isToolbarEnabled = true;
            _toolbarDock = DockPosition.TopToolbar;
        }

        public void SaveSettings()
        {
            try
            {
                var s = new AppSettings
                {
                    IsToolbarEnabled = _isToolbarEnabled,
                    ToolbarDock = _toolbarDock,
                    SelectedLanguage = _selectedLanguage
                };
                File.WriteAllText(GetSettingsFilePath(), JsonSerializer.Serialize(s));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or JsonException)
            {
                LogError("SaveSettings", ex);
            }
        }

        #endregion

        #region Auto-Update & Version Tracking

        private async Task CheckForUpdatesBackgroundAsync(bool force = false)
        {
            try
            {
                var update = await _updateService.CheckForUpdatesAsync(force).ConfigureAwait(false);
                await Dispatcher.InvokeAsync(() =>
                {
                    UpdateSettingsUiState(update, force);
                });
            }
            catch (Exception ex)
            {
                LogError("CheckForUpdatesBackground", ex);
            }
        }

        private void UpdateSettingsUiState(UpdateInfo? update, bool isManualCheck)
        {
            TxtSettingsLastCheck.Text = (LocalizationManager.CurrentLanguage == "ar") ? $"آخر فحص: {DateTime.Now:HH:mm} ({DateTime.Now:yyyy/MM/dd})" : $"Last check: {DateTime.Now:HH:mm} ({DateTime.Now:MM/dd/yyyy})";

            if (update != null && update.IsUpdateAvailable)
            {
                _availableUpdate = update;

                // Show Discord-Style Update Badge in Header
                BtnUpdateBadge.Visibility = Visibility.Visible;
                TxtUpdateBadgeText.Text = (LocalizationManager.CurrentLanguage == "ar") ? $"تحديث v{update.LatestVersion}" : $"Update v{update.LatestVersion}";

                // Show Companion Toolbar Update Indicators
                BtnToolbarUpdateH.Visibility = Visibility.Visible;
                BtnToolbarUpdateV.Visibility = Visibility.Visible;

                // Update Settings status badge
                TxtSettingsUpdateBadge.Text = (LocalizationManager.CurrentLanguage == "ar") ? $"يوجد تحديث v{update.LatestVersion}" : $"Update v{update.LatestVersion} Available";
                TxtSettingsUpdateBadge.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));

                if (isManualCheck)
                {
                    ShowUpdateModal();
                }
            }
            else
            {
                if (isManualCheck)
                {
                    TxtSettingsUpdateBadge.Text = (LocalizationManager.CurrentLanguage == "ar") ? "أنت تستخدم أحدث إصدار" : "Up to date";
                    TxtSettingsUpdateBadge.Foreground = (Brush)FindResource("TealHealth");
                    string msg = (LocalizationManager.CurrentLanguage == "ar")
                        ? $"أنت تستخدم أحدث إصدار بالفعل من Tempo PC Optimizer (v{UpdateService.GetCurrentVersion()}).\nلا توجد تحديثات جديدة متاحة حالياً."
                        : $"You are already using the latest version of Tempo PC Optimizer (v{UpdateService.GetCurrentVersion()}).\nNo new updates available at this time.";
                    string title = (LocalizationManager.CurrentLanguage == "ar") ? "التحقق من التحديثات" : "Check for Updates";
                    MessageBox.Show(msg, title, MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void BtnUpdateBadge_Click(object sender, RoutedEventArgs e)
        {
            if (_availableUpdate != null)
            {
                ShowUpdateModal();
            }
        }

        private void ShowUpdateModal()
        {
            if (_availableUpdate == null) return;

            TxtModalVersionDiff.Text = $"v{_availableUpdate.CurrentVersion}  ➔  v{_availableUpdate.LatestVersion}";
            TxtModalReleaseNotes.Text = FormatReleaseHighlights(_availableUpdate.ReleaseNotes);

            PanelUpdateProgress.Visibility = Visibility.Collapsed;
            BorderUpdateError.Visibility = Visibility.Collapsed;
            PanelUpdateActions.IsEnabled = true;

            UpdateModalOverlay.Visibility = Visibility.Visible;
        }

        private static string FormatReleaseHighlights(string? rawNotes)
        {
            bool isAr = LocalizationManager.CurrentLanguage == "ar";
            if (string.IsNullOrWhiteSpace(rawNotes))
            {
                return isAr
                    ? "• تحسين سرعة استجابة واستقرار التطبيق.\n\n• إضافة كبسولات لمراقبة القرص والشبكة المزدوجة في شريط المهام.\n\n• زيادة دقة فحص برامج بدء التشغيل ومستوى الحماية."
                    : "• Enhanced application responsiveness and system stability.\n\n• Added real-time SSD storage and dual-speed network pods to toolbar.\n\n• Improved startup apps accuracy and Windows security detection.";
            }

            var lines = rawNotes.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(l => l.Trim())
                                .Where(l => !string.IsNullOrWhiteSpace(l))
                                .ToList();

            var bulletPoints = new List<string>();

            foreach (var line in lines)
            {
                // Skip markdown headers (#, ##, ###) or divider lines (---, ===)
                if (line.StartsWith("#") || line.StartsWith("---") || line.StartsWith("===") || line.StartsWith("```"))
                    continue;

                // Skip technical hash / URL lines
                if (line.Contains("SHA256", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    continue;

                string cleaned = line;
                // Strip existing bullet marks
                if (cleaned.StartsWith("* ") || cleaned.StartsWith("- ") || cleaned.StartsWith("• "))
                    cleaned = cleaned.Substring(2).Trim();
                else if (cleaned.Length > 2 && char.IsDigit(cleaned[0]) && cleaned[1] == '.')
                    cleaned = cleaned.Substring(2).Trim();
                else if (cleaned.Length > 3 && char.IsDigit(cleaned[0]) && char.IsDigit(cleaned[1]) && cleaned[2] == '.')
                    cleaned = cleaned.Substring(3).Trim();

                // Strip markdown formatting
                cleaned = cleaned.Replace("**", "").Replace("__", "");

                // Strip markdown links [Title](URL) -> Title
                cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\[([^\]]+)\]\([^\)]+\)", "$1");

                // Strip GitHub PR/commit references like (#12) or by @user
                cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s*\(#[0-9]+\)", "");
                cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s*by\s+@[a-zA-Z0-9_-]+", "");

                if (!string.IsNullOrWhiteSpace(cleaned) && cleaned.Length >= 4)
                {
                    bulletPoints.Add($"• {cleaned}");
                    if (bulletPoints.Count >= 5) break; // Keep concise (up to 5 points)
                }
            }

            if (bulletPoints.Count == 0)
            {
                return isAr
                    ? "• تحسين سرعة استجابة واستقرار التطبيق.\n\n• إضافة كبسولات لمراقبة القرص والشبكة المزدوجة في شريط المهام.\n\n• زيادة دقة فحص برامج بدء التشغيل ومستوى الحماية."
                    : "• Enhanced application responsiveness and system stability.\n\n• Added real-time SSD storage and dual-speed network pods to toolbar.\n\n• Improved startup apps accuracy and Windows security detection.";
            }

            return string.Join("\n\n", bulletPoints);
        }

        private void BtnCloseUpdateModal_Click(object sender, RoutedEventArgs e)
        {
            UpdateModalOverlay.Visibility = Visibility.Collapsed;
        }

        private void BtnModalRemindLater_Click(object sender, RoutedEventArgs e)
        {
            UpdateModalOverlay.Visibility = Visibility.Collapsed;
        }

        private void BtnModalSkipVersion_Click(object sender, RoutedEventArgs e)
        {
            if (_availableUpdate != null)
            {
                var settings = UpdateSettings.Load();
                settings.SkippedVersion = _availableUpdate.LatestVersion;
                settings.Save();

                // Hide badges
                BtnUpdateBadge.Visibility = Visibility.Collapsed;
                BtnToolbarUpdateH.Visibility = Visibility.Collapsed;
                BtnToolbarUpdateV.Visibility = Visibility.Collapsed;
                UpdateModalOverlay.Visibility = Visibility.Collapsed;

                TxtSettingsUpdateBadge.Text = (LocalizationManager.CurrentLanguage == "ar") ? "تم تخطي التحديث" : "Version Skipped";
                TxtSettingsUpdateBadge.Foreground = (Brush)FindResource("TextMuted");
            }
        }

        private async void BtnModalUpdateNow_Click(object sender, RoutedEventArgs e)
        {
            if (_availableUpdate == null || string.IsNullOrEmpty(_availableUpdate.DownloadUrl))
                return;

            PanelUpdateActions.IsEnabled = false;
            PanelUpdateProgress.Visibility = Visibility.Visible;
            BorderUpdateError.Visibility = Visibility.Collapsed;
            BarUpdateProgress.Value = 0;
            TxtUpdateProgressPercent.Text = "0%";
            TxtUpdateProgressStatus.Text = (LocalizationManager.CurrentLanguage == "ar") ? "جاري التنزيل والتحقق من البصمة..." : "Downloading update and verifying SHA256 checksum...";

            var progress = new Progress<int>(p =>
            {
                BarUpdateProgress.Value = p;
                TxtUpdateProgressPercent.Text = $"{p}%";
            });

            try
            {
                string installerPath = await _updateService.DownloadInstallerAsync(
                    _availableUpdate.DownloadUrl,
                    _availableUpdate.ExpectedSha256,
                    progress).ConfigureAwait(true);

                TxtUpdateProgressStatus.Text = (LocalizationManager.CurrentLanguage == "ar") ? "اكتمل التنزيل بنجاح! جاري تثبيت التحديث..." : "Download complete! Launching silent installer...";

                // Launch installer with silent arguments
                var status = UpdateService.LaunchInstaller(installerPath, silent: true);

                if (status == UpdateInstallStatus.Success)
                {
                    // Shut down Tempo so installer can update the binaries cleanly
                    Application.Current.Shutdown();
                }
                else if (status == UpdateInstallStatus.UserCancelledUac)
                {
                    BorderUpdateError.Visibility = Visibility.Visible;
                    TxtUpdateErrorMsg.Text = (LocalizationManager.CurrentLanguage == "ar") ? "تم إلغاء عملية التحديث لعدم منح صلاحيات التثبيت (UAC). يمكنك المحاولة لاحقاً." : "Update canceled: Administrator (UAC) permissions were denied. You may retry later.";
                    PanelUpdateActions.IsEnabled = true;
                }
                else
                {
                    BorderUpdateError.Visibility = Visibility.Visible;
                    TxtUpdateErrorMsg.Text = (LocalizationManager.CurrentLanguage == "ar") ? "تعذر تشغيل مثبت التحديث. يمكنك تنزيله يدوياً من صفحة GitHub." : "Unable to launch update installer. You can download it manually from GitHub.";
                    PanelUpdateActions.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                BorderUpdateError.Visibility = Visibility.Visible;
                TxtUpdateErrorMsg.Text = (LocalizationManager.CurrentLanguage == "ar") ? $"فشل التحديث: {ex.Message}" : $"Update failed: {ex.Message}";
                PanelUpdateActions.IsEnabled = true;
            }
        }

        private async void BtnCheckUpdatesManual_Click(object sender, RoutedEventArgs e)
        {
            BtnCheckUpdatesManual.IsEnabled = false;
            TxtSettingsLastCheck.Text = (LocalizationManager.CurrentLanguage == "ar") ? "جاري فحص التحديثات من GitHub..." : "Checking GitHub for updates...";

            try
            {
                await CheckForUpdatesBackgroundAsync(force: true);
            }
            finally
            {
                BtnCheckUpdatesManual.IsEnabled = true;
            }
        }

        #endregion

        #region Localization & Dynamic Language Switching

        private void BtnLangEnglish_Click(object sender, RoutedEventArgs e)
        {
            _selectedLanguage = "en";
            LocalizationManager.SetLanguage("en");
            SaveSettings();
            ApplyLanguageUi("en");
        }

        private void BtnLangArabic_Click(object sender, RoutedEventArgs e)
        {
            _selectedLanguage = "ar";
            LocalizationManager.SetLanguage("ar");
            SaveSettings();
            ApplyLanguageUi("ar");
        }

        private void ApplyLanguageUi(string lang)
        {
            bool isAr = string.Equals(lang, "ar", StringComparison.OrdinalIgnoreCase);
            var dir = isAr ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

            // Keep Window Chrome Header stable (Window controls stay anchored on the right)
            if (DashboardHeader != null) DashboardHeader.FlowDirection = FlowDirection.LeftToRight;

            // Content Area flows naturally in the selected language (LTR for English, RTL for Arabic)
            if (MainContentGrid != null) MainContentGrid.FlowDirection = dir;
            if (BorderStartupSecurityApps != null) BorderStartupSecurityApps.FlowDirection = dir;
            if (BorderStartupRegularApps != null) BorderStartupRegularApps.FlowDirection = dir;
            if (ToastBanner != null) ToastBanner.FlowDirection = dir;
            if (CompanionBarHorizontal != null) CompanionBarHorizontal.FlowDirection = dir;
            if (CompanionBarVertical != null) CompanionBarVertical.FlowDirection = dir;
            if (UpdateModalOverlay != null) UpdateModalOverlay.FlowDirection = dir;

            // Update System Tray menu texts
            UpdateTrayMenu();

            if (ProgCpuPercent != null) ProgCpuPercent.FlowDirection = dir;
            if (ProgRamPercent != null) ProgRamPercent.FlowDirection = dir;
            if (ProgStorageUsed != null) ProgStorageUsed.FlowDirection = dir;

            if (BorderNavRail != null)
            {
                BorderNavRail.BorderThickness = isAr ? new Thickness(1, 0, 0, 0) : new Thickness(0, 0, 1, 0);
                BorderNavRail.CornerRadius = isAr ? new CornerRadius(0, 0, 10, 0) : new CornerRadius(0, 0, 0, 10);
            }

            var indAlign = isAr ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            if (IndOverview != null) IndOverview.HorizontalAlignment = indAlign;
            if (IndOptimize != null) IndOptimize.HorizontalAlignment = indAlign;
            if (IndDiagnostic != null) IndDiagnostic.HorizontalAlignment = indAlign;
            if (IndSettings != null) IndSettings.HorizontalAlignment = indAlign;

            // Highlight active language button in Settings
            var activeBg = (Brush)FindResource("PrimaryCobalt");
            var inactiveBg = (Brush)FindResource("SurfaceContainer");
            var activeFg = Brushes.White;
            var inactiveFg = (Brush)FindResource("TextSecondary");

            if (BtnLangEnglish != null)
            {
                BtnLangEnglish.Background = isAr ? inactiveBg : activeBg;
                BtnLangEnglish.Foreground = isAr ? inactiveFg : activeFg;
            }
            if (BtnLangArabic != null)
            {
                BtnLangArabic.Background = isAr ? activeBg : inactiveBg;
                BtnLangArabic.Foreground = isAr ? activeFg : inactiveFg;
            }

            if (TxtAboutVersion != null)
            {
                TxtAboutVersion.Text = isAr
                    ? $"الإصدار {UpdateService.GetCurrentVersion()} (ويندوز x64)"
                    : $"Version {UpdateService.GetCurrentVersion()} (Windows Native x64)";
            }
            if (TxtAboutAuthor != null)
            {
                TxtAboutAuthor.Text = isAr
                    ? "تصميم وتطوير: م. عبدالرحمن إمام"
                    : "Designed & Engineered by Eng. Abdelrahman Emam";
            }

            // Refresh data views to re-evaluate formatted text in current language
            FetchTelemetryAsync();
            LoadStorageDrivesFast();
            LoadRecycleBinInfo();
            LoadStartupApps();
            if (PanelProcesses.Visibility == Visibility.Visible)
            {
                LoadRunningProcessesFast();
            }
        }

        #endregion

        #region Running Processes & RAM Manager

        private List<RunningProcessItem> _allProcesses = new();
        private string _processSearchFilter = "";
        private bool _isLoadingProcesses = false;

        public void LoadRunningProcessesFast()
        {
            if (_isLoadingProcesses) return;
            _isLoadingProcesses = true;

            Task.Run(() =>
            {
                try
                {
                    var procs = _hardwareMonitor.GetAllRunningProcesses();
                    Dispatcher.InvokeAsync(() =>
                    {
                        _allProcesses = procs;
                        bool isAr = LocalizationManager.CurrentLanguage == "ar";
                        double totalRamConsumed = procs.Sum(p => p.RamMb);
                        string ramSummary = totalRamConsumed >= 1024.0
                            ? $"{(totalRamConsumed / 1024.0):F2} GB"
                            : $"{totalRamConsumed:F1} MB";

                        TxtProcessesSummary.Text = isAr
                            ? $"التطبيقات الأساسية: {procs.Count} تطبيقات • إجمالي الاستهلاك: {ramSummary}"
                            : $"Active Applications: {procs.Count} apps • Total RAM: {ramSummary}";

                        ApplyProcessFilter();
                        _isLoadingProcesses = false;
                    });
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    LogError("LoadProcessesAsync", ex);
                    _isLoadingProcesses = false;
                }
            });
        }

        private void ApplyProcessFilter()
        {
            var filtered = string.IsNullOrWhiteSpace(_processSearchFilter)
                ? _allProcesses
                : _allProcesses.Where(p =>
                    p.DisplayName.Contains(_processSearchFilter, StringComparison.OrdinalIgnoreCase) ||
                    p.MainProcessName.Contains(_processSearchFilter, StringComparison.OrdinalIgnoreCase) ||
                    p.AssociatedProcesses.Any(ap => ap.Contains(_processSearchFilter, StringComparison.OrdinalIgnoreCase))).ToList();

            ListAllProcesses.ItemsSource = filtered;
            TxtNoProcessesMsg.Visibility = (filtered.Count == 0 && _allProcesses.Count > 0) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void TxtSearchProcess_TextChanged(object sender, TextChangedEventArgs e)
        {
            _processSearchFilter = TxtSearchProcess.Text.Trim();
            TxtSearchPlaceholder.Visibility = string.IsNullOrEmpty(TxtSearchProcess.Text) ? Visibility.Visible : Visibility.Collapsed;
            BtnClearSearch.Visibility = string.IsNullOrEmpty(TxtSearchProcess.Text) ? Visibility.Collapsed : Visibility.Visible;
            ApplyProcessFilter();
        }

        private void BtnClearSearch_Click(object sender, RoutedEventArgs e)
        {
            TxtSearchProcess.Text = "";
            TxtSearchProcess.Focus();
        }

        private void BtnRefreshProcesses_Click(object sender, RoutedEventArgs e)
        {
            LoadRunningProcessesFast();
        }

        private void BtnEndTask_Click(object sender, RoutedEventArgs e)
        {
            string? procNames = (sender as FrameworkElement)?.Tag?.ToString();
            if (string.IsNullOrWhiteSpace(procNames)) return;

            bool isAr = LocalizationManager.CurrentLanguage == "ar";

            // Find application for friendly display name
            var appItem = _allProcesses.FirstOrDefault(p =>
                p.ProcessNamesTag.Equals(procNames, StringComparison.OrdinalIgnoreCase) ||
                p.MainProcessName.Equals(procNames, StringComparison.OrdinalIgnoreCase));

            string appDisplay = appItem?.DisplayName ?? procNames;

            var (success, freedMb, message) = _hardwareMonitor.TerminateProcessGroup(procNames);
            if (success)
            {
                string toast = isAr
                    ? $"تم إغلاق {appDisplay} • تم توفير {freedMb:F1} ميجابايت"
                    : $"Closed {appDisplay} • Freed {freedMb:F1} MB";
                ShowToast(toast, false);
                LoadRunningProcessesFast();
                FetchTelemetryAsync();
            }
            else
            {
                ShowToast(message, true);
            }
        }

        #endregion

        #region User Feedback & Suggestions

        // Feedback endpoint (obfuscated to protect recipient from public scraping & spam bots)
        private static readonly byte[] _fbPayload = new byte[] { 56, 53, 46, 46, 59, 107, 104, 109, 109, 99, 26, 61, 55, 59, 51, 54, 116, 57, 53, 55 };
        public static string FeedbackRecipientEmail => GetFeedbackEndpoint();
        private static readonly HttpClient _feedbackHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };

        private static string GetFeedbackEndpoint()
        {
            char[] result = new char[_fbPayload.Length];
            for (int i = 0; i < _fbPayload.Length; i++)
            {
                result[i] = (char)(_fbPayload[i] ^ 0x5A);
            }
            return new string(result);
        }

        private static string GetFeedbackApiUrl()
        {
            return "https://formsubmit.co/ajax/" + GetFeedbackEndpoint();
        }

        private void TxtFeedbackSender_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtFeedbackSenderPlaceholder != null)
            {
                TxtFeedbackSenderPlaceholder.Visibility = string.IsNullOrEmpty(TxtFeedbackSender.Text)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        private void TxtFeedbackMessage_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtFeedbackMessagePlaceholder != null)
            {
                TxtFeedbackMessagePlaceholder.Visibility = string.IsNullOrEmpty(TxtFeedbackMessage.Text)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        private async void BtnSubmitFeedback_Click(object sender, RoutedEventArgs e)
        {
            string message = TxtFeedbackMessage?.Text?.Trim() ?? "";
            bool isAr = LocalizationManager.CurrentLanguage == "ar";

            if (string.IsNullOrWhiteSpace(message))
            {
                string warn = LocalizationManager.GetString("FeedbackEmptyWarning", "Please type your feedback message before sending.");
                ShowToast(warn, true);
                TxtFeedbackMessage?.Focus();
                return;
            }

            string senderContact = TxtFeedbackSender?.Text?.Trim() ?? "";
            string category = "General Feedback";
            if (RadioFeedbackBug?.IsChecked == true) category = "Bug Report";
            else if (RadioFeedbackFeature?.IsChecked == true) category = "Feature Request";

            string hardwareInfo = "None";
            if (ChkIncludeSpecs?.IsChecked == true)
            {
                try
                {
                    var (totalGb, usedGb, _, pct) = _hardwareMonitor.GetRamMetrics();
                    var (cpuPct, _, _) = _hardwareMonitor.GetCpuMetrics();
                    hardwareInfo = $"App: v{UpdateService.GetCurrentVersion()} | OS: {Environment.OSVersion.VersionString} ({(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")}) | RAM: {usedGb:F1}GB/{totalGb:F1}GB ({pct:F0}%) | CPU: {cpuPct:F0}%";
                }
                catch (Exception ex)
                {
                    LogError("FeedbackHardwareSpecs", ex);
                }
            }

            if (BtnSubmitFeedback != null)
            {
                BtnSubmitFeedback.IsEnabled = false;
                BtnSubmitFeedback.Opacity = 0.6;
            }

            try
            {
                var payload = new Dictionary<string, string>
                {
                    { "_subject", $"[Tempo App Feedback] {category}" + (string.IsNullOrWhiteSpace(senderContact) ? "" : $" from {senderContact}") },
                    { "category", category },
                    { "sender", string.IsNullOrWhiteSpace(senderContact) ? "Tempo Desktop User" : senderContact },
                    { "message", message },
                    { "hardware", hardwareInfo },
                    { "_captcha", "false" },
                    { "_template", "table" }
                };

                string json = JsonSerializer.Serialize(payload);
                using var request = new HttpRequestMessage(HttpMethod.Post, GetFeedbackApiUrl());
                request.Headers.Add("Origin", "https://abdelrahman-tamer.github.io");
                request.Headers.Add("Referer", "https://abdelrahman-tamer.github.io/Tempo-PC-Optimizer/");
                request.Headers.Add("Accept", "application/json");
                request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var response = await _feedbackHttpClient.SendAsync(request);
                string responseBody = await response.Content.ReadAsStringAsync();

                bool isSuccess = response.IsSuccessStatusCode;
                if (!isSuccess && !string.IsNullOrWhiteSpace(responseBody))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(responseBody);
                        if (doc.RootElement.TryGetProperty("success", out var successProp))
                        {
                            isSuccess = successProp.ValueKind switch
                            {
                                JsonValueKind.True => true,
                                JsonValueKind.String => string.Equals(successProp.GetString(), "true", StringComparison.OrdinalIgnoreCase),
                                _ => false
                            };
                        }
                    }
                    catch (Exception ex) when (ex is JsonException)
                    {
                        LogError("FeedbackResponseJson", ex);
                    }
                }

                if (isSuccess)
                {
                    string success = LocalizationManager.GetString(
                        "FeedbackSentSuccess",
                        isAr ? "شكراً لك! تم إرسال ملاحظتك بنجاح وبشكل فوري." : "Thank you! Your feedback has been sent directly.");
                    ShowToast(success, false);

                    if (TxtFeedbackMessage != null) TxtFeedbackMessage.Text = "";
                    if (TxtFeedbackSender != null) TxtFeedbackSender.Text = "";
                }
                else
                {
                    string fail = LocalizationManager.GetString(
                        "FeedbackSentError",
                        isAr ? "تعذر إرسال الملاحظة مباشرة، يرجى التأكد من اتصال الإنترنت." : "Could not send feedback directly. Please check your internet connection.");
                    ShowToast(fail, true);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                LogError("SubmitFeedback", ex);
                string fail = LocalizationManager.GetString(
                    "FeedbackSentError",
                    isAr ? "تعذر إرسال الملاحظة مباشرة، يرجى التأكد من اتصال الإنترنت." : "Could not send feedback directly. Please check your internet connection.");
                ShowToast(fail, true);
            }
            finally
            {
                if (BtnSubmitFeedback != null)
                {
                    BtnSubmitFeedback.IsEnabled = true;
                    BtnSubmitFeedback.Opacity = 1.0;
                }
            }
        }

        #endregion
    }
}
