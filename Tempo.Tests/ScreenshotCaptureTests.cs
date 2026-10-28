using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Tempo;
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
                    LocalizationManager.Initialize("en");

                    var win = new MainWindow();
                    win.ShowInTaskbar = false;
                    win.Left = -3000;
                    win.Top = -3000;
                    win.Show();

                    WaitDispatcher(2000);

                    string outDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "docs", "assets"));

                    // 1. Overview (Dashboard with live hardware specs & telemetry)
                    win.ShowDashboardView();
                    win.SelectTab("Overview");
                    WaitDispatcher(1200);
                    CaptureElement(win.MainDashboardView, Path.Combine(outDir, "screen_overview.png"), 880, 1400);

                    // 2. Processes (Apps & RAM with icons, monograms, group count)
                    win.SelectTab("Processes");
                    WaitDispatcher(1800);
                    CaptureElement(win.MainDashboardView, Path.Combine(outDir, "screen_processes.png"), 880, 1400);

                    // 3. Optimize (Clean & Boost)
                    win.SelectTab("Optimize");
                    WaitDispatcher(800);
                    CaptureElement(win.MainDashboardView, Path.Combine(outDir, "screen_optimize.png"), 880, 1400);

                    // 4. Diagnostic (Hardware Deep Diagnostics)
                    win.SelectTab("Diagnostic");
                    WaitDispatcher(800);
                    CaptureElement(win.MainDashboardView, Path.Combine(outDir, "screen_diagnostic.png"), 880, 1400);

                    // 5. Feedback
                    win.SelectTab("Feedback");
                    WaitDispatcher(600);
                    CaptureElement(win.MainDashboardView, Path.Combine(outDir, "screen_feedback.png"), 880, 1400);

                    // 6. Startup Apps / Settings
                    win.SelectTab("Settings");
                    WaitDispatcher(1000);
                    CaptureElement(win.MainDashboardView, Path.Combine(outDir, "screen_startup.png"), 880, 1400);

                    // 7. Horizontal Toolbar (Clean 375px Pill)
                    win._toolbarDock = DockPosition.TopToolbar;
                    win.ShowToolbarView();
                    win.CompanionBarHorizontal.Visibility = Visibility.Visible;
                    win.CompanionBarVertical.Visibility = Visibility.Collapsed;
                    win.MainDashboardView.Visibility = Visibility.Collapsed;
                    WaitDispatcher(800);
                    CaptureElement(win.CompanionBarHorizontal, Path.Combine(outDir, "screen_toolbar_top.png"), 750, 76);

                    // 8. Vertical Toolbar (Clean 38px Sidebar)
                    win._toolbarDock = DockPosition.Side;
                    win.ShowToolbarView();
                    win.CompanionBarHorizontal.Visibility = Visibility.Collapsed;
                    win.CompanionBarVertical.Visibility = Visibility.Visible;
                    win.MainDashboardView.Visibility = Visibility.Collapsed;
                    WaitDispatcher(800);
                    CaptureElement(win.CompanionBarVertical, Path.Combine(outDir, "screen_toolbar_side.png"), 76, 590);

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

            string checkFile = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "docs", "assets", "screen_overview.png"));
            Assert.True(File.Exists(checkFile));
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
            double w = el.ActualWidth > 0 ? el.ActualWidth : targetWidth / 2.0;
            double h = el.ActualHeight > 0 ? el.ActualHeight : targetHeight / 2.0;

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
