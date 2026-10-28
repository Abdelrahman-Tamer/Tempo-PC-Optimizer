using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Tempo;
using Tempo.Models;
using Tempo.Services;
using Xunit;

namespace Tempo.Tests
{
    public class ScreenshotCaptureTests
    {
        [Fact]
        public void GenerateFreshShowcaseScreenshots()
        {
            Exception? threadEx = null;
            var t = new Thread(() =>
            {
                try
                {
                    var app = Application.Current ?? new Application();
                    app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                    string assetsDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "docs", "assets"));
                    string enDir = Path.Combine(assetsDir, "en");
                    string arDir = Path.Combine(assetsDir, "ar");
                    Directory.CreateDirectory(enDir);
                    Directory.CreateDirectory(arDir);

                    var win = new MainWindow();
                    win.PauseBackgroundTimers();
                    win.ShowInTaskbar = false;
                    win.Left = -3000;
                    win.Top = -3000;
                    win.Show();

                    WaitDispatcher(800);

                    // 1. Capture English Suite
                    CaptureSuiteForWindow(win, "en", enDir, assetsDir);

                    // 2. Capture Arabic Suite
                    CaptureSuiteForWindow(win, "ar", arDir, null);

                    win.Close();
                }
                catch (Exception ex)
                {
                    threadEx = ex;
                }
            });

            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();

            if (threadEx != null) throw new Exception("Screenshot generation failed: " + threadEx.Message, threadEx);

            string checkEn = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "docs", "assets", "en", "screen_overview.png"));
            string checkAr = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "docs", "assets", "ar", "screen_overview.png"));
            Assert.True(File.Exists(checkEn), "English screen_overview.png must exist");
            Assert.True(File.Exists(checkAr), "Arabic screen_overview.png must exist");
        }

        private static void CaptureSuiteForWindow(MainWindow win, string lang, string targetDir, string? fallbackDir)
        {
            LocalizationManager.SetLanguage(lang);
            win.ApplyLanguageUi(lang);

            WaitDispatcher(500);
            PopulateRichShowcaseData(win, lang);
            WaitDispatcher(500);

            // 1. Overview
            win.ShowDashboardView();
            win.SelectTab("Overview");
            PopulateRichShowcaseData(win, lang);
            WaitDispatcher(600);
            CaptureAndSave(win.MainDashboardView, "screen_overview.png", 880, 1400, targetDir, fallbackDir);

            // 2. Processes (Apps & RAM)
            win.SelectTab("Processes");
            PopulateRichShowcaseData(win, lang);
            WaitDispatcher(600);
            CaptureAndSave(win.MainDashboardView, "screen_processes.png", 880, 1400, targetDir, fallbackDir);

            // 3. Optimize (Clean & Boost)
            win.SelectTab("Optimize");
            PopulateRichShowcaseData(win, lang);
            WaitDispatcher(600);
            CaptureAndSave(win.MainDashboardView, "screen_optimize.png", 880, 1400, targetDir, fallbackDir);

            // 4. Diagnostic (Hardware Sensors)
            win.SelectTab("Diagnostic");
            PopulateRichShowcaseData(win, lang);
            WaitDispatcher(600);
            CaptureAndSave(win.MainDashboardView, "screen_diagnostic.png", 880, 1400, targetDir, fallbackDir);

            // 5. Feedback
            win.SelectTab("Feedback");
            PopulateRichShowcaseData(win, lang);
            WaitDispatcher(400);
            CaptureAndSave(win.MainDashboardView, "screen_feedback.png", 880, 1400, targetDir, fallbackDir);

            // 6. Startup Apps / Settings
            win.SelectTab("Settings");
            PopulateRichShowcaseData(win, lang);
            WaitDispatcher(600);
            CaptureAndSave(win.MainDashboardView, "screen_startup.png", 880, 1400, targetDir, fallbackDir);

            // 7. Horizontal Companion Toolbar (375px Pill)
            win._toolbarDock = DockPosition.TopToolbar;
            win.ShowToolbarView();
            win.CompanionBarHorizontal.Visibility = Visibility.Visible;
            win.CompanionBarVertical.Visibility = Visibility.Collapsed;
            win.MainDashboardView.Visibility = Visibility.Collapsed;
            PopulateRichShowcaseData(win, lang);
            WaitDispatcher(600);
            CaptureAndSave(win.CompanionBarHorizontal, "screen_toolbar_top.png", 750, 76, targetDir, fallbackDir);

            // 8. Vertical Companion Toolbar (38px Sidebar)
            win._toolbarDock = DockPosition.Side;
            win.ShowToolbarView();
            win.CompanionBarHorizontal.Visibility = Visibility.Collapsed;
            win.CompanionBarVertical.Visibility = Visibility.Visible;
            win.MainDashboardView.Visibility = Visibility.Collapsed;
            PopulateRichShowcaseData(win, lang);
            WaitDispatcher(600);
            CaptureAndSave(win.CompanionBarVertical, "screen_toolbar_side.png", 76, 590, targetDir, fallbackDir);
        }

        private static void PopulateRichShowcaseData(MainWindow win, string lang)
        {
            bool isAr = lang == "ar";
            win.ApplyLanguageUi(lang);

            var teal = (SolidColorBrush)win.FindResource("TealHealth");

            // Health Status & Scores
            if (win.TxtHealthStatus != null)
                win.TxtHealthStatus.Text = isAr ? "استهلاك الموارد: منخفض ومستقر" : "Resource Usage: Low & Optimal";

            if (win.TxtHealthPercent != null)
                win.TxtHealthPercent.Text = "96%";

            // CPU
            if (win.TxtCpuPercent != null) win.TxtCpuPercent.Text = "14%";
            if (win.TxtCpuClockTemp != null) win.TxtCpuClockTemp.Text = "2.60 GHz • 44°C";
            if (win.ProgCpuPercent != null) win.ProgCpuPercent.Value = 14;

            // RAM
            if (win.TxtRamPercentValue != null) win.TxtRamPercentValue.Text = "38%";
            if (win.TxtRamUsageFormatted != null)
                win.TxtRamUsageFormatted.Text = isAr ? "\u200E12.2 GB\u200E من أصل \u200E32.0 GB\u200E" : "\u200E12.2 GB\u200E of \u200E32.0 GB\u200E";
            if (win.ProgRamPercent != null) win.ProgRamPercent.Value = 38;

            // Storage
            if (win.TxtStorageFree != null)
                win.TxtStorageFree.Text = isAr ? "\u200E245 GB\u200E متاح من أصل \u200E512 GB\u200E" : "\u200E245 GB\u200E free of \u200E512 GB\u200E";
            if (win.ProgStorageUsed != null) win.ProgStorageUsed.Value = 52;

            // Quick Actions & Observed Junk (Overview)
            if (win.TxtRecTempSize != null) win.TxtRecTempSize.Text = "1.42 GB";
            if (win.TxtRecStartupCount != null) win.TxtRecStartupCount.Text = isAr ? "4 برامج" : "4 apps";
            if (win.TxtRecRecycleBinSize != null) win.TxtRecRecycleBinSize.Text = "380 MB";

            // Hardware Specs Card (Diagnostic)
            if (win.TxtDeviceModel != null) win.TxtDeviceModel.Text = "Dell XPS 15";
            if (win.PerfCpuInfo != null) win.PerfCpuInfo.Text = "Intel Core i7-10750H @ 2.60 GHz";
            if (win.PerfGpuInfo != null) win.PerfGpuInfo.Text = "NVIDIA GeForce GTX 1650 Ti";
            if (win.PerfVramInfo != null) win.PerfVramInfo.Text = "1.2 GB / 4.0 GB";
            if (win.PerfRamSpecs != null) win.PerfRamSpecs.Text = "32 GB DDR4 @ 2667 MHz • 2 Slots";
            if (win.PerfUptimeInfo != null) win.PerfUptimeInfo.Text = isAr ? "1 يوم و 4 س" : "1d 4h 22m";
            if (win.PerfPowerInfo != null) win.PerfPowerInfo.Text = isAr ? "88% (جارٍ الشحن ⚡)" : "88% (Charging ⚡)";
            if (win.PerfNetDown != null) win.PerfNetDown.Text = "↓ 12.4 MB/s";
            if (win.PerfNetUp != null) win.PerfNetUp.Text = "↑ 2.1 MB/s";
            if (win.PerfPingInfo != null) win.PerfPingInfo.Text = "• 14 ms";

            // Optimize Tab (Detailed Junk Cleanup)
            if (win.TxtScanSummary != null)
                win.TxtScanSummary.Text = isAr ? "جاهز للتنظيف • تم رصد 2.18 GB مخلفات قابلة للتفريغ" : "Ready for cleanup • 2.18 GB reclaimable junk detected";
            if (win.TxtTempDetails != null)
                win.TxtTempDetails.Text = isAr ? "ملفات مؤقتة وكاش نظام (1.42 GB)" : "Temp files & system cache (1.42 GB)";
            if (win.TxtRecycleBinDetails != null)
                win.TxtRecycleBinDetails.Text = isAr ? "سلة المحذوفات (380 MB • 12 عنصر)" : "Recycle Bin (380 MB • 12 items)";
            if (win.TxtBrowserDetails != null)
                win.TxtBrowserDetails.Text = isAr ? "كاش المتصفحات المؤقت (320 MB)" : "Browser cache files (320 MB)";
            if (win.TxtDevDetails != null)
                win.TxtDevDetails.Text = isAr ? "مخلفات بيئات التطوير (65 MB)" : "Developer cache files (65 MB)";

            // Overview Top 5 Processes
            if (win.ListTopProcesses != null)
            {
                win.ListTopProcesses.ItemsSource = new List<ProcessViewModel>
                {
                    new ProcessViewModel { ProcessName = "chrome", DisplayName = "Google Chrome", RamMb = 840 },
                    new ProcessViewModel { ProcessName = "code", DisplayName = "Visual Studio Code", RamMb = 620 },
                    new ProcessViewModel { ProcessName = "Discord", DisplayName = "Discord", RamMb = 380 },
                    new ProcessViewModel { ProcessName = "Telegram", DisplayName = "Telegram Desktop", RamMb = 290 },
                    new ProcessViewModel { ProcessName = "Spotify", DisplayName = "Spotify Music", RamMb = 210 },
                };
            }

            // Processes Tab (Full List)
            if (win.TxtProcessesSummary != null)
            {
                win.TxtProcessesSummary.Text = isAr
                    ? "البرامج والتطبيقات الشغالة: 8 برامج • إجمالي الاستهلاك: 2.85 GB"
                    : "Running Applications: 8 apps • Total RAM: 2.85 GB";
            }

            if (win.ListAllProcesses != null)
            {
                win.ListAllProcesses.ItemsSource = new List<RunningProcessItem>
                {
                    new RunningProcessItem { AppKey = "chrome", DisplayName = "Google Chrome", MainProcessName = "chrome", RamMb = 840, InstanceCount = 8, HasActiveWindow = true },
                    new RunningProcessItem { AppKey = "code", DisplayName = "Visual Studio Code", MainProcessName = "code", RamMb = 620, InstanceCount = 4, HasActiveWindow = true },
                    new RunningProcessItem { AppKey = "discord", DisplayName = "Discord", MainProcessName = "Discord", RamMb = 380, InstanceCount = 3, HasActiveWindow = false },
                    new RunningProcessItem { AppKey = "telegram", DisplayName = "Telegram", MainProcessName = "Telegram", RamMb = 290, InstanceCount = 1, HasActiveWindow = true },
                    new RunningProcessItem { AppKey = "spotify", DisplayName = "Spotify", MainProcessName = "Spotify", RamMb = 215, InstanceCount = 2, HasActiveWindow = false },
                    new RunningProcessItem { AppKey = "slack", DisplayName = "Slack", MainProcessName = "slack", RamMb = 195, InstanceCount = 3, HasActiveWindow = false },
                    new RunningProcessItem { AppKey = "terminal", DisplayName = "Windows Terminal", MainProcessName = "WindowsTerminal", RamMb = 125, InstanceCount = 1, HasActiveWindow = true },
                    new RunningProcessItem { AppKey = "notion", DisplayName = "Notion", MainProcessName = "Notion", RamMb = 110, InstanceCount = 2, HasActiveWindow = false },
                };
            }

            // Storage Drives
            if (win.ListStorageDrives != null)
            {
                win.ListStorageDrives.ItemsSource = new List<StorageDriveViewModel>
                {
                    new StorageDriveViewModel { DriveLetter = "C:", VolumeLabel = "Windows (OS)", TotalGb = 512, FreeGb = 245, UsedPercent = 52, MediaType = "SSD" },
                    new StorageDriveViewModel { DriveLetter = "D:", VolumeLabel = "Data & Projects", TotalGb = 1024, FreeGb = 680, UsedPercent = 34, MediaType = "SSD" }
                };
            }

            // Settings Tab (Startup Apps)
            if (win.ListStartupRegularApps != null)
            {
                win.ListStartupRegularApps.ItemsSource = new List<StartupAppItem>
                {
                    new StartupAppItem { Name = "Steam Client Bootstrapper", Impact = StartupImpactLevel.High, IsEnabled = true, Location = "HKCU\\Run" },
                    new StartupAppItem { Name = "Discord", Impact = StartupImpactLevel.High, IsEnabled = true, Location = "HKCU\\Run" },
                    new StartupAppItem { Name = "Spotify", Impact = StartupImpactLevel.Medium, IsEnabled = true, Location = "HKCU\\Run" },
                    new StartupAppItem { Name = "Telegram Desktop", Impact = StartupImpactLevel.Medium, IsEnabled = true, Location = "HKCU\\Run" },
                    new StartupAppItem { Name = "Microsoft OneDrive", Impact = StartupImpactLevel.Low, IsEnabled = true, Location = "HKCU\\Run" },
                };
            }
            if (win.ListStartupSecurityApps != null)
            {
                win.ListStartupSecurityApps.ItemsSource = new List<StartupAppItem>
                {
                    new StartupAppItem { Name = "Windows Security notification icon", Impact = StartupImpactLevel.Low, IsEnabled = true, Location = "HKLM\\Run", IsSystemManaged = true }
                };
            }

            // Companion Bar Telemetry
            if (win.BarCpuTextH != null) win.BarCpuTextH.Text = "14%";
            if (win.BarRamTextH != null) win.BarRamTextH.Text = "38%";
            if (win.BarCpuTextV != null) win.BarCpuTextV.Text = "14%";
            if (win.BarRamTextV != null) win.BarRamTextV.Text = "38%";
            if (win.BarCpuKpiArcH != null)
            {
                win.BarCpuKpiArcH.Data = MainWindow.CreateArcGeometry(14.0, 14.0, 11.25, 14);
                win.BarCpuKpiArcH.Stroke = teal;
            }
            if (win.BarRamKpiArcH != null)
            {
                win.BarRamKpiArcH.Data = MainWindow.CreateArcGeometry(14.0, 14.0, 11.25, 38);
                win.BarRamKpiArcH.Stroke = teal;
            }
            if (win.BarCpuKpiArcV != null)
            {
                win.BarCpuKpiArcV.Data = MainWindow.CreateArcGeometry(14.0, 14.0, 11.25, 14);
                win.BarCpuKpiArcV.Stroke = teal;
            }
            if (win.BarRamKpiArcV != null)
            {
                win.BarRamKpiArcV.Data = MainWindow.CreateArcGeometry(14.0, 14.0, 11.25, 38);
                win.BarRamKpiArcV.Stroke = teal;
            }
        }

        private static void CaptureAndSave(FrameworkElement el, string filename, int targetWidth, int targetHeight, string targetDir, string? fallbackDir)
        {
            string primaryPath = Path.Combine(targetDir, filename);
            CaptureElement(el, primaryPath, targetWidth, targetHeight);

            if (!string.IsNullOrEmpty(fallbackDir))
            {
                string fallbackPath = Path.Combine(fallbackDir, filename);
                File.Copy(primaryPath, fallbackPath, true);
            }
        }

        private static void WaitDispatcher(int ms)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                frame.Continue = false;
            };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }

        private static void CaptureElement(FrameworkElement el, string outFile, int targetWidth, int targetHeight)
        {
            double w = el.ActualWidth > 0 ? el.ActualWidth : (el.Width > 0 ? el.Width : targetWidth / 2.0);
            double h = el.ActualHeight > 0 ? el.ActualHeight : (el.Height > 0 ? el.Height : targetHeight / 2.0);

            el.Measure(new Size(w, h));
            el.Arrange(new Rect(new Size(w, h)));
            el.UpdateLayout();

            double dpiX = (targetWidth / w) * 96.0;
            double dpiY = (targetHeight / h) * 96.0;

            var rtb = new RenderTargetBitmap(targetWidth, targetHeight, dpiX, dpiY, PixelFormats.Pbgra32);
            rtb.Render(el);

            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(outFile);
            enc.Save(fs);
        }
    }
}
