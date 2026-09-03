using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
using Tempo.Services;
using Tempo.Models;

// Disambiguate System.Windows.Forms types
using TrayNotifyIcon = System.Windows.Forms.NotifyIcon;
using TrayContextMenuStrip = System.Windows.Forms.ContextMenuStrip;
using TrayToolStripSeparator = System.Windows.Forms.ToolStripSeparator;
using TrayToolTipIcon = System.Windows.Forms.ToolTipIcon;
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
        public string MediaType { get; set; } = "SSD";
        public string SpaceSummary => LocalizationManager.FormatStorageSummary(FreeGb, TotalGb, UsedPercent);

        public Visibility TrimVisibility => Visibility.Visible;
        public string ActionButtonText => MediaType.Equals("HDD", StringComparison.OrdinalIgnoreCase)
            ? (LocalizationManager.CurrentLanguage == "ar" ? "إلغاء التجزئة" : "Defrag")
            : (LocalizationManager.CurrentLanguage == "ar" ? "تنشيط TRIM" : "Run TRIM");

        public Brush BadgeBg => MediaType switch
        {
            "SSD" => new SolidColorBrush(Color.FromArgb(40, 0, 102, 255)),
            "HDD" => new SolidColorBrush(Color.FromArgb(40, 68, 221, 193)),
            _ => new SolidColorBrush(Color.FromArgb(40, 0, 102, 255))
        };
        public Brush BadgeBorder => MediaType switch
        {
            "SSD" => new SolidColorBrush(Color.FromArgb(80, 0, 102, 255)),
            "HDD" => new SolidColorBrush(Color.FromArgb(80, 68, 221, 193)),
            _ => new SolidColorBrush(Color.FromArgb(80, 0, 102, 255))
        };
        public Brush BadgeFg => MediaType switch
        {
            "SSD" => new SolidColorBrush(Color.FromArgb(255, 179, 197, 255)),
            "HDD" => new SolidColorBrush(Color.FromArgb(255, 68, 221, 193)),
            _ => new SolidColorBrush(Color.FromArgb(255, 179, 197, 255))
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
        public readonly HardwareMonitorService _hardwareMonitor;
        public readonly CleanupService _cleanupService;
        private readonly DispatcherTimer _telemetryTimer;
        private readonly DispatcherTimer _autoHideTimer;
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
        private const double BarWidthH = 520.0;
        private const double BarHeightH = 42.0;
        private const double BarWidthV = 34.0;
        private const double BarHeightV = 255.0;

        public MainWindow()
        {
            InitializeComponent();

            LoadAppIconAndLogos();

            _hardwareMonitor = new HardwareMonitorService();
            _cleanupService = new CleanupService();

            LoadSettings();
            LocalizationManager.Initialize(_selectedLanguage);
            LocalizationManager.LanguageChanged += lang => ApplyLanguageUi(lang);
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

            // Pause polling completely during minimize (0% idle CPU)
            this.StateChanged += (s, e) =>
            {
                if (this.WindowState == WindowState.Minimized)
                {
                    _telemetryTimer.Stop();
                }
                else if (this.WindowState == WindowState.Normal)
                {
                    _telemetryTimer.Start();
                    FetchTelemetryAsync();
                }
            };

// Post-startup settleTimer removed to eliminate page-fault degradation

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
                    try { this.DragMove(); } catch { }
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
            catch { }
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

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
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
            catch { }

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
            catch { }
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
            catch { }

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

        public void ExitApp()
        {
            // Immediate visual vanishing (<1ms)
            this.Hide();

            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }

            _telemetryTimer?.Stop();
            _autoHideTimer?.Stop();

            // Instant clean process exit
            Environment.Exit(0);
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

            _currentView = AppViewMode.Toolbar;
            MainDashboardView.Visibility = Visibility.Collapsed;
            this.ShowInTaskbar = false;
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
        public void TabNavSettings_Click(object sender, RoutedEventArgs e) => SelectTab("Settings");

        public void SelectTab(string tabName)
        {
            // 1. Instantly toggle visibility (0ms UI latency)
            PanelOverview.Visibility = Visibility.Collapsed;
            PanelOptimize.Visibility = Visibility.Collapsed;
            PanelDiagnostic.Visibility = Visibility.Collapsed;
            PanelSettings.Visibility = Visibility.Collapsed;

            // 2. Reset Indicators and colors
            IndOverview.Visibility = Visibility.Collapsed;
            IndOptimize.Visibility = Visibility.Collapsed;
            IndDiagnostic.Visibility = Visibility.Collapsed;
            IndSettings.Visibility = Visibility.Collapsed;

            var muted = (SolidColorBrush)FindResource("TextSecondary");
            var accent = (SolidColorBrush)FindResource("PrimaryAccent");

            IconNavOverview.Fill = muted; TxtNavOverview.Foreground = muted;
            IconNavOptimize.Fill = muted; TxtNavOptimize.Foreground = muted;
            IconNavDiagnostic.Fill = muted; TxtNavDiagnostic.Foreground = muted;
            IconNavSettings.Fill = muted; TxtNavSettings.Foreground = muted;

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

                case "Settings":
                    PanelSettings.Visibility = Visibility.Visible;
                    IndSettings.Visibility = Visibility.Visible;
                    IconNavSettings.Fill = accent; TxtNavSettings.Foreground = accent;
                    LoadStartupApps();
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
                catch
                {
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
                    CardCpuBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF5252"));
                    CardCpuBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#291517"));
                }
                else if (cpuPercent >= 65)
                {
                    CardCpuBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFA726"));
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
                    CardRamBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF5252"));
                    CardRamBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#291517"));
                }
                else if (ramPercent >= 65)
                {
                    CardRamBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFA726"));
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
            if (BarNetSpeedH != null) BarNetSpeedH.Text = $"↓ {downStr.Replace(" ", "")}";

            if (BarRamTextV != null) { BarRamTextV.Text = $"{ramPercent:F0}%"; BarRamTextV.Foreground = ramBrush; }
            if (BarRamIconV != null) BarRamIconV.Fill = ramBrush;
            if (BarCpuTextV != null) { BarCpuTextV.Text = $"{cpuPercent:F0}%"; BarCpuTextV.Foreground = cpuBrush; }
            if (BarCpuIconV != null) BarCpuIconV.Fill = cpuBrush;
            if (BarNetSpeedV != null) BarNetSpeedV.Text = downStr.Contains("MB") ? $"{downStr.Split(' ')[0]}M" : $"{downStr.Split(' ')[0]}K";

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
                Dispatcher.InvokeAsync(() =>
                {
                    var securityApps = apps.Where(a => a.IsSecurityApp).ToList();
                    var regularApps = apps.Where(a => !a.IsSecurityApp).ToList();

                    if (ListStartupSecurityApps != null) ListStartupSecurityApps.ItemsSource = securityApps;
                    if (ListStartupRegularApps != null) ListStartupRegularApps.ItemsSource = regularApps;

                    TxtRecStartupCount.Text = (LocalizationManager.CurrentLanguage == "ar")
                        ? $"{apps.Count} تطبيق"
                        : $"{apps.Count} Apps";
                    if (TxtSecurityAppsCount != null) TxtSecurityAppsCount.Text = $"{securityApps.Count}";
                    if (TxtRegularAppsCount != null) TxtRegularAppsCount.Text = $"{regularApps.Count}";
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
            catch
            {
                try { Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true }); }
                catch (Exception ex) { ShowToast((LocalizationManager.CurrentLanguage == "ar") ? $"تعذر فتح إعدادات بدء التشغيل: {ex.Message}" : $"Unable to open Startup Settings: {ex.Message}", true); }
            }
        }

        private void BtnDisableStartupApp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is StartupAppItem app)
            {
                if (!app.IsUserScope)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true });
                        ShowToast((LocalizationManager.CurrentLanguage == "ar") ? "تطبيقات النظام (HKLM) تتطلب إدارتها عبر نافذة ويندوز." : "System apps (HKLM) must be managed via Windows Settings.", false);
                    }
                    catch { ShowToast((LocalizationManager.CurrentLanguage == "ar") ? "يتطلب تعديل هذا التطبيق صلاحيات مسؤول." : "Modifying this app requires Administrator privileges.", true); }
                    return;
                }

                // Check for Security / System sensitive applications
                bool isSecurityApp = app.Name.IndexOf("Security", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     app.Name.IndexOf("Defender", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     app.Name.IndexOf("Antivirus", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     app.Name.IndexOf("Avast", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     app.Name.IndexOf("Kaspersky", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     app.Name.IndexOf("Bitdefender", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     app.Name.IndexOf("Malware", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     app.Name.IndexOf("ESET", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     app.Name.IndexOf("Norton", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     app.Name.IndexOf("Firewall", StringComparison.OrdinalIgnoreCase) >= 0;

                bool isAr = (LocalizationManager.CurrentLanguage == "ar");
                string securityNotice = isSecurityApp
                    ? (isAr ? "\n\n⚠️ تحذير أمني شديد: هذا البرنامج يبدو مرتبطاً بالحماية أو مكافحة الفيروسات! تعطيله قد يؤثر على أمان النظام."
                            : "\n\n⚠️ High Security Warning: This application appears to be security/antivirus related! Disabling it may compromise system safety.")
                    : "";

                string title = isAr ? "إيقاف برنامج بدء التشغيل" : "Disable Startup App";
                string msg = isAr
                    ? $"هل تريد إيقاف تشغيل {app.Name} تلقائياً عند فتح الجهاز؟" + securityNotice
                    : $"Stop {app.Name} from starting automatically with Windows?" + securityNotice;

                var confirm = MessageBox.Show(
                    msg,
                    title,
                    MessageBoxButton.YesNo,
                    isSecurityApp ? MessageBoxImage.Warning : MessageBoxImage.Question,
                    MessageBoxResult.No);

                if (confirm != MessageBoxResult.Yes) return;

                bool ok = _hardwareMonitor.DisableStartupApp(app);
                if (ok)
                {
                    ShowToast((LocalizationManager.CurrentLanguage == "ar") ? $"تم تعطيل {app.Name}" : $"{app.Name} disabled", false);
                    LoadStartupApps();
                }
                else
                {
                    ShowToast((LocalizationManager.CurrentLanguage == "ar") ? $"تعذر إزالة {app.Name}. يمكنك إدارته عبر إعدادات ويندوز." : $"Unable to disable {app.Name}. You can manage it via Windows Settings.", true);
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

            Task.Run(() =>
            {
                var ramRes = _cleanupService.OptimizeRamWorkingSets();
                var tempRes = _cleanupService.QuickCleanTemp();

                Dispatcher.InvokeAsync(() =>
                {
                    if (btn != null) btn.IsEnabled = true;
                    ShowToast((LocalizationManager.CurrentLanguage == "ar") ? $"تم التسريع: تحرير \u200E{ramRes.ReclaimedMb:F1} MB\u200E رام و \u200E{tempRes.ReclaimedMb:F1} MB\u200E مؤقت" : $"Boosted: \u200E{ramRes.ReclaimedMb:F1} MB\u200E RAM & \u200E{tempRes.ReclaimedMb:F1} MB\u200E temp freed", false);
                    FetchTelemetryAsync();
                    // Re-query actual storage and recycle bin state instead of false hardcoded 0 MB
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

        private void BtnQuickClean_Click(object sender, RoutedEventArgs e)
        {
            var res = _cleanupService.QuickCleanTemp();
            ShowToast(res.Message, false);
            LoadRecycleBinInfo();
            TxtRecTempSize.Text = "0 MB";
            TxtTempDetails.Text = (LocalizationManager.CurrentLanguage == "ar") ? "تم تنظيف الملفات المؤقتة" : "Temporary files cleaned";
        }

        private void BtnEmptyRecycleBin_Click(object sender, RoutedEventArgs e)
        {
            var before = _cleanupService.QueryRecycleBin();
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

            var res = _cleanupService.EmptyRecycleBin();
            ShowToast(res.Message, !res.Success);
            LoadRecycleBinInfo();
        }

        private void BtnBrowserClean_Click(object sender, RoutedEventArgs e)
        {
            var res = _cleanupService.BrowserCacheFlush();
            ShowToast(res.Message, !res.Success);
            TxtBrowserDetails.Text = (LocalizationManager.CurrentLanguage == "ar") ? "تم تنظيف كاش المتصفح" : "Browser cache cleaned";
        }

        private void BtnDevClean_Click(object sender, RoutedEventArgs e)
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
                var res = _cleanupService.DevCachesFlush();
                ShowToast(res.Message, false);
                TxtDevDetails.Text = (LocalizationManager.CurrentLanguage == "ar") ? "تم تنظيف كاش المطورين" : "Developer cache cleaned";
            }
        }

        private void BtnSsdTrim_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is StorageDriveViewModel vm)
            {
                string letter = vm.DriveLetter.TrimEnd(':', '\\', '/');
                bool isSsd = vm.MediaType.Equals("SSD", StringComparison.OrdinalIgnoreCase);

                bool isAr = (LocalizationManager.CurrentLanguage == "ar");
                string actionMsg = isSsd
                    ? (isAr ? $"جاري تنشيط خلايا SSD عبر TRIM للقرص {letter}:... يرجى الانتظار." : $"Optimizing SSD via TRIM on drive {letter}:... please wait.")
                    : (isAr ? $"جاري تحسين وإلغاء تجزئة القرص {letter}:... يرجى الانتظار." : $"Optimizing and defragmenting drive {letter}:... please wait.");

                ShowToast(actionMsg, false);
                btn.IsEnabled = false;

                Task.Run(() =>
                {
                    var res = _cleanupService.SsdReTrim(letter);
                    Dispatcher.InvokeAsync(() =>
                    {
                        btn.IsEnabled = true;
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
                var brush = (SolidColorBrush)FindResource(isWarning ? "AmberWarn" : "TealHealth");
                string iconData = isWarning
                    ? "M12,2 C6.48,2 2,6.48 2,12 C2,17.52 6.48,22 12,22 C17.52,22 22,17.52 22,12 C22,6.48 17.52,2 12,2 Z M13,17 L11,17 L11,15 L13,15 L13,17 Z M13,13 L11,13 L11,7 L13,7 L13,13 Z"
                    : "M12,2 C6.48,2 2,6.48 2,12 C2,17.52 6.48,22 12,22 C17.52,22 22,17.52 22,12 C22,6.48 17.52,2 12,2 Z M10,17 L5,12 L6.41,10.59 L10,14.17 L17.59,6.58 L19,8 L10,17 Z";

                _toastTimer?.Stop();

                if (_currentView == AppViewMode.Toolbar)
                {
                    ToastBanner.Visibility = Visibility.Collapsed;

                    if (ToolbarToastH != null && ToolbarMetricsContainerH != null)
                    {
                        ToolbarToastTextH.Text = message;
                        ToolbarToastTextH.Foreground = brush;
                        ToolbarToastH.BorderBrush = brush;
                        if (ToolbarToastIconH != null)
                        {
                            ToolbarToastIconH.Fill = brush;
                            ToolbarToastIconH.Data = Geometry.Parse(iconData);
                        }

                        ToolbarMetricsContainerH.Visibility = Visibility.Collapsed;
                        ToolbarToastH.Visibility = Visibility.Visible;

                        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
                        _toastTimer.Tick += (s, e) =>
                        {
                            _toastTimer.Stop();
                            ToolbarToastH.Visibility = Visibility.Collapsed;
                            ToolbarMetricsContainerH.Visibility = Visibility.Visible;
                        };
                        _toastTimer.Start();
                    }
                }
                else
                {
                    if (ToolbarToastH != null) ToolbarToastH.Visibility = Visibility.Collapsed;
                    if (ToolbarMetricsContainerH != null) ToolbarMetricsContainerH.Visibility = Visibility.Visible;

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

                    _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
                    _toastTimer.Tick += (s, e) =>
                    {
                        _toastTimer.Stop();
                        ToastBanner.Visibility = Visibility.Collapsed;
                    };
                    _toastTimer.Start();
                }
            }
            catch { }
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

        public void BtnToolbarBoost_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var da = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(550))
                {
                    EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
                };
                if (ToolbarBoostIconH != null) ToolbarBoostIconH.RenderTransform.BeginAnimation(RotateTransform.AngleProperty, da);
                if (ToolbarBoostIconV != null) ToolbarBoostIconV.RenderTransform.BeginAnimation(RotateTransform.AngleProperty, da);
            }
            catch { }

            BtnHeroOptimize_Click(sender, e);
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
                ShowToast((LocalizationManager.CurrentLanguage == "ar") ? "تم تثبيت الشريط على الشاشة" : "Mini Bar pinned", false);
            }
            else
            {
                _autoHideTimer.Start();
                ShowToast((LocalizationManager.CurrentLanguage == "ar") ? "تم تفعيل الإخفاء التلقائي" : "Auto-hide enabled", false);
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

                try { this.DragMove(); } catch { }

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
                var res = _cleanupService.OptimizeRamWorkingSets();
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
            catch { }

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
            catch { }
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
            catch
            {
                // Non-blocking update failure
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
            TxtModalReleaseNotes.Text = string.IsNullOrWhiteSpace(_availableUpdate.ReleaseNotes)
                ? ((LocalizationManager.CurrentLanguage == "ar") ? "تحديث جديد يتضمن تحسينات في الأداء وسرعة الاستجابة واستقرار النظام." : "New update featuring performance improvements, responsiveness and system stability.")
                : _availableUpdate.ReleaseNotes;

            PanelUpdateProgress.Visibility = Visibility.Collapsed;
            BorderUpdateError.Visibility = Visibility.Collapsed;
            PanelUpdateActions.IsEnabled = true;

            UpdateModalOverlay.Visibility = Visibility.Visible;
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
            var activeBg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0066FF"));
            var inactiveBg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#161922"));
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
        }

        #endregion

        #endregion
    }
}
