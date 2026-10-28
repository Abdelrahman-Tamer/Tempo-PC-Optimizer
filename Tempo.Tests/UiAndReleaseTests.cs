using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tempo;
using Tempo.Models;
using Tempo.Services;
using Xunit;

namespace Tempo.Tests
{
    public class UiAndReleaseTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   \t\r\n  ")]
        public void FormatReleaseHighlights_ReturnsHonestFallbackOnEmptyInput(string? input)
        {
            string formatted = MainWindow.FormatReleaseHighlights(input);
            Assert.False(string.IsNullOrWhiteSpace(formatted));
            Assert.True(formatted.Contains("No release notes provided", StringComparison.OrdinalIgnoreCase) ||
                        formatted.Contains("لا توجد ملاحظات إصدار", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void FormatReleaseHighlights_ExtractsMarkdownBullets()
        {
            string markdown = @"
### What's New in v2.2.5
- High-contrast text update for WCAG 2.2 AA
- Excluded protected system services
- Added self-elevating TRIM handler
- Setup.exe installer watchdog
SHA256: 1234567890abcdef
https://example.com/download
";
            string formatted = MainWindow.FormatReleaseHighlights(markdown);

            // Clean bullet preserved
            Assert.Contains("Excluded protected system services", formatted);

            // Headings present
            Assert.Contains("Changes:", formatted);

            // Denylist terms must be absent (case-insensitive)
            Assert.DoesNotContain("WCAG", formatted, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("TRIM", formatted, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(".exe", formatted, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("watchdog", formatted, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SHA256", formatted, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("https://", formatted, StringComparison.OrdinalIgnoreCase);

            // Tail text present
            Assert.Contains("Full details on GitHub", formatted);
        }

        [Fact]
        public void FormatReleaseHighlights_EnforcesCapAndDenylistRephrasing()
        {
            string markdown = @"
### What's New
- Added new setting option
- Added another UI preference
- Added third extra change
- Improved speed and rendering
- Optimized memory and cache
- Boosted responsiveness further
- Fixed crash on close
- Solved error during startup
- Fixed memory leak on exit
- Used runas with --set-approved
";
            string formatted = MainWindow.FormatReleaseHighlights(markdown);

            // All three headings present
            Assert.Contains("Changes:", formatted);
            Assert.Contains("Improvements:", formatted);
            Assert.Contains("Fixes:", formatted);

            // Count bullets: at most 2 per section (total <= 6)
            int bulletCount = formatted.Split('\n').Count(line => line.Trim().StartsWith("• "));
            Assert.InRange(bulletCount, 3, 6);

            // Denylist terms absent
            Assert.DoesNotContain("runas", formatted, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("--set-approved", formatted, StringComparison.OrdinalIgnoreCase);

            // Tail text present
            Assert.Contains("Full details on GitHub", formatted);
        }

        [Fact]
        public void FormatReleaseHighlights_HandlesRealReleaseBodyV225()
        {
            string bodyPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "dist", "RELEASE-BODY-v2.2.5.md"));
            if (File.Exists(bodyPath))
            {
                string rawBody = File.ReadAllText(bodyPath);
                string formattedEn = MainWindow.FormatReleaseHighlights(rawBody);

                // Denylist absent
                Assert.DoesNotContain("runas", formattedEn, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("--set-approved", formattedEn, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("--measure-boot", formattedEn, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("--trim", formattedEn, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("watchdog", formattedEn, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("AllowUnsafeBlocks", formattedEn, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("0x06", formattedEn, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("IsSafePath", formattedEn, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("SHA256", formattedEn, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("http://", formattedEn, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("https://", formattedEn, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("UAC", formattedEn, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("TRIM", formattedEn, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("WCAG", formattedEn, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(".exe", formattedEn, StringComparison.OrdinalIgnoreCase);

                // Cap enforced: at most 6 bullets total
                int bullets = formattedEn.Split('\n').Count(l => l.Trim().StartsWith("• "));
                Assert.InRange(bullets, 1, 6);

                // Tail present
                Assert.Contains("Full details on GitHub", formattedEn);
            }
        }

        [Fact]
        public void FormatReleaseHighlights_HandlesRealReleaseBodyV226()
        {
            string bodyPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "dist", "RELEASE-BODY-v2.2.6.md"));
            if (File.Exists(bodyPath))
            {
                string rawBody = File.ReadAllText(bodyPath);
                string formattedEn = MainWindow.FormatReleaseHighlights(rawBody);

                Assert.False(string.IsNullOrWhiteSpace(formattedEn));
                Assert.DoesNotContain("SHA256", formattedEn, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("https://", formattedEn, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(".exe", formattedEn, StringComparison.OrdinalIgnoreCase);

                // Cap enforced: at most 6 bullets total
                int bullets = formattedEn.Split('\n').Count(l => l.Trim().StartsWith("• "));
                Assert.InRange(bullets, 1, 6);

                // Tail present
                Assert.Contains("Full details on GitHub", formattedEn);
            }
        }

        [Fact]
        public void FormatReleaseHighlights_HandlesRealReleaseBodyV227()
        {
            string bodyPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "dist", "RELEASE-BODY-v2.2.8.md"));
            if (File.Exists(bodyPath))
            {
                string rawBody = File.ReadAllText(bodyPath);
                string formattedEn = MainWindow.FormatReleaseHighlights(rawBody);

                Assert.False(string.IsNullOrWhiteSpace(formattedEn));
                Assert.DoesNotContain("SHA256", formattedEn, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("https://", formattedEn, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(".exe", formattedEn, StringComparison.OrdinalIgnoreCase);

                // Cap enforced: at most 6 bullets total
                int bullets = formattedEn.Split('\n').Count(l => l.Trim().StartsWith("• "));
                Assert.InRange(bullets, 1, 6);

                // Tail present
                Assert.Contains("Full details on GitHub", formattedEn);
            }
        }

        [Fact]
        public void FormatReleaseHighlights_ExtractsSnippetWhenNoBulletsPresent()
        {
            string headersAndUrlsOnly = "# Release v2.2.5\n## Security Updates\nhttps://github.com/repo/releases";
            string formatted = MainWindow.FormatReleaseHighlights(headersAndUrlsOnly);

            Assert.True(formatted.Contains("No release notes provided", StringComparison.OrdinalIgnoreCase) ||
                        formatted.Contains("لا توجد ملاحظات إصدار", StringComparison.OrdinalIgnoreCase));
            Assert.Contains("# Release v2.2.5", formatted);
        }

        [Fact]
        public void UpdateInfo_CurrentVersion_IsVersion227()
        {
            var updateInfo = new UpdateInfo();
            Assert.Equal("2.2.8", updateInfo.CurrentVersion);
        }

        [Fact]
        public void UpdateService_GetCurrentVersion_Matches227()
        {
            var version = UpdateService.GetCurrentVersion();
            Assert.Equal(new Version(2, 2, 8), version);
        }

        [Fact]
        public void CompanionBar_HorizontalWidth_IsConfiguredToMinimalistPill()
        {
            var field = typeof(MainWindow).GetField("BarWidthH", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.NotNull(field);
            double width = (double)field.GetValue(null)!;
            Assert.Equal(375.0, width);
        }

        [Fact]
        public void CompanionBar_VerticalDimensions_IsConfiguredToMinimalistPill()
        {
            var widthField = typeof(MainWindow).GetField("BarWidthV", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.NotNull(widthField);
            double width = (double)widthField.GetValue(null)!;
            Assert.Equal(38.0, width);

            var heightField = typeof(MainWindow).GetField("BarHeightV", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.NotNull(heightField);
            double height = (double)heightField.GetValue(null)!;
            Assert.Equal(295.0, height);
        }

        [Fact]
        public void HardwareMonitor_GetStorageMetricsWithDriveType_ResolvesReadyDrives()
        {
            var hw = new HardwareMonitorService();
            var drives = hw.GetStorageMetricsWithDriveType();
            Assert.NotNull(drives);
            Assert.NotEmpty(drives);
            foreach (var d in drives)
            {
                Assert.False(string.IsNullOrWhiteSpace(d.DriveLetter));
                Assert.True(d.TotalGb > 0);
            }
            var cDrive = drives.FirstOrDefault(d => d.DriveLetter.StartsWith("C", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(cDrive);
            Assert.Equal("SSD", cDrive.MediaType);
        }

        [Fact]
        public void HardwareMonitor_GetStartupAppsAndBootPerformance_DoesNotThrow()
        {
            var hw = new HardwareMonitorService();
            var apps = hw.GetStartupApps();
            Assert.NotNull(apps);
            Assert.NotEmpty(apps);
            var boot = hw.GetBootPerformanceInfo(apps);
            Assert.NotNull(boot);
            Assert.True(boot.BiosTimeSeconds.HasValue);
            Assert.True(boot.Uptime.HasValue);
            Assert.True(boot.LastBootUpTime.HasValue);
        }

        [Fact]
        public void HardwareMonitor_IsSystemOrServiceProcess_CorrectlyFiltersCoreWindowsDaemons()
        {
            // Core Windows services must be flagged as system/service
            Assert.True(HardwareMonitorService.IsSystemOrServiceProcess("svchost", @"C:\Windows\System32\svchost.exe", IntPtr.Zero, ""));
            Assert.True(HardwareMonitorService.IsSystemOrServiceProcess("sihost", @"C:\Windows\System32\sihost.exe", IntPtr.Zero, ""));
            Assert.True(HardwareMonitorService.IsSystemOrServiceProcess("dwm", @"C:\Windows\System32\dwm.exe", IntPtr.Zero, ""));
            Assert.True(HardwareMonitorService.IsSystemOrServiceProcess("taskhostw", @"C:\Windows\System32\taskhostw.exe", IntPtr.Zero, ""));
            Assert.True(HardwareMonitorService.IsSystemOrServiceProcess("runtimebroker", @"C:\Windows\System32\runtimebroker.exe", IntPtr.Zero, ""));

            // User applications and allowed utilities must NOT be flagged as system/service
            Assert.False(HardwareMonitorService.IsSystemOrServiceProcess("notepad", @"C:\Windows\System32\notepad.exe", IntPtr.Zero, ""));
            Assert.False(HardwareMonitorService.IsSystemOrServiceProcess("chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe", IntPtr.Zero, ""));
            Assert.False(HardwareMonitorService.IsSystemOrServiceProcess("telegram", @"C:\Users\DELL\AppData\Roaming\Telegram Desktop\Telegram.exe", IntPtr.Zero, ""));
            Assert.False(HardwareMonitorService.IsSystemOrServiceProcess("discord", @"C:\Users\DELL\AppData\Local\Discord\app-1.0.9001\Discord.exe", IntPtr.Zero, ""));
        }

        [Fact]
        public void HardwareMonitor_GetAllRunningProcesses_ReturnsOnlyUserAppsAndFiltersServices()
        {
            var hw = new HardwareMonitorService();
            var procs = hw.GetAllRunningProcesses();
            Assert.NotNull(procs);

            var blockedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "svchost", "dwm", "csrss", "sihost", "taskhostw", "services", "wininit", "smss", "lsass"
            };

            foreach (var p in procs)
            {
                Assert.False(blockedNames.Contains(p.MainProcessName), $"Found protected service '{p.MainProcessName}' in user apps list!");
                Assert.False(string.IsNullOrWhiteSpace(p.DisplayName));
                Assert.False(string.IsNullOrWhiteSpace(p.AppKey));
                Assert.True(p.RamMb >= 0);
            }
        }

        [Fact]
        public void RunningProcessItem_SubtitleText_FormatsActiveWindowAndBackgroundCorrectly()
        {
            // English Test
            LocalizationManager.Initialize("en");
            var itemWindowEn = new RunningProcessItem
            {
                MainProcessName = "Telegram",
                HasActiveWindow = true,
                InstanceCount = 1
            };
            Assert.Equal("Active Window", itemWindowEn.SubtitleText);

            var itemBgEn = new RunningProcessItem
            {
                MainProcessName = "Discord",
                HasActiveWindow = false,
                InstanceCount = 3
            };
            Assert.Equal("Background App • 3 processes", itemBgEn.SubtitleText);

            // Arabic Test
            LocalizationManager.Initialize("ar");
            var itemWindowAr = new RunningProcessItem
            {
                MainProcessName = "Telegram",
                HasActiveWindow = true,
                InstanceCount = 2
            };
            Assert.Equal("نافذة نشطة • 2 عمليات", itemWindowAr.SubtitleText);

            var itemBgAr = new RunningProcessItem
            {
                MainProcessName = "Discord",
                HasActiveWindow = false,
                InstanceCount = 1
            };
            Assert.Equal("يعمل في الخلفية", itemBgAr.SubtitleText);
        }

        [Fact]
        public void ToolbarCloseBehavior_MustHideNotMinimizeToAvoidGlitch()
        {
            // Verify that MainWindow.xaml.cs avoids WindowState.Minimized when closing toolbar.
            // When ShowInTaskbar is false and WindowStyle is None, Win32 DWM parks minimized
            // windows in the bottom-left corner of the screen, creating a visual glitch.
            string mwPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "MainWindow.xaml.cs");
            Assert.True(File.Exists(mwPath), $"MainWindow.xaml.cs must exist at {mwPath}");

            string code = File.ReadAllText(mwPath);

            // BtnToolbarClose_Click must use Hide() and NOT set WindowState.Minimized
            int handlerIdx = code.IndexOf("public void BtnToolbarClose_Click");
            Assert.True(handlerIdx >= 0, "BtnToolbarClose_Click must be declared");

            int handlerEnd = code.IndexOf("}", handlerIdx);
            string handlerBody = code.Substring(handlerIdx, handlerEnd - handlerIdx);

            Assert.Contains(".Hide()", handlerBody);
            Assert.DoesNotContain("WindowState = WindowState.Minimized", handlerBody);
        }

        [Fact]
        public void Localization_EnglishAndArabic_ContainMatchingKeys()
        {
            string enPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Resources", "Strings.en.xaml");
            string arPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Resources", "Strings.ar.xaml");

            Assert.True(File.Exists(enPath), $"Strings.en.xaml must exist at {enPath}");
            Assert.True(File.Exists(arPath), $"Strings.ar.xaml must exist at {arPath}");

            var regex = new System.Text.RegularExpressions.Regex(@"x:Key=""([^""]+)""", System.Text.RegularExpressions.RegexOptions.Compiled);

            var enKeys = regex.Matches(File.ReadAllText(enPath)).Select(m => m.Groups[1].Value).ToHashSet();
            var arKeys = regex.Matches(File.ReadAllText(arPath)).Select(m => m.Groups[1].Value).ToHashSet();

            var missingInAr = enKeys.Except(arKeys).ToList();
            var missingInEn = arKeys.Except(enKeys).ToList();

            Assert.Empty(missingInAr);
            Assert.Empty(missingInEn);
        }

        [Fact]
        public void MainWindow_DynamicResourceStrings_AllExistInLocalizationDictionaries()
        {
            string mwPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "MainWindow.xaml");
            string enPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Resources", "Strings.en.xaml");

            Assert.True(File.Exists(mwPath), $"MainWindow.xaml must exist at {mwPath}");
            Assert.True(File.Exists(enPath), $"Strings.en.xaml must exist at {enPath}");

            var keyRegex = new System.Text.RegularExpressions.Regex(@"x:Key=""([^""]+)""", System.Text.RegularExpressions.RegexOptions.Compiled);
            var enKeys = keyRegex.Matches(File.ReadAllText(enPath)).Select(m => m.Groups[1].Value).ToHashSet();

            string xaml = File.ReadAllText(mwPath);
            var dynRegex = new System.Text.RegularExpressions.Regex(@"DynamicResource\s+([A-Za-z0-9_]+)", System.Text.RegularExpressions.RegexOptions.Compiled);
            var dynKeys = dynRegex.Matches(xaml).Select(m => m.Groups[1].Value).Distinct().ToList();

            // Ignore brushes, colors, fonts, styles, and internal XAML keys
            var stringDynKeys = dynKeys.Where(k =>
                !k.EndsWith("Brush", StringComparison.OrdinalIgnoreCase) &&
                !k.EndsWith("Color", StringComparison.OrdinalIgnoreCase) &&
                !k.EndsWith("Style", StringComparison.OrdinalIgnoreCase) &&
                !k.EndsWith("Font", StringComparison.OrdinalIgnoreCase) &&
                !k.Contains("Template", StringComparison.OrdinalIgnoreCase) &&
                !k.Contains("Glow", StringComparison.OrdinalIgnoreCase) &&
                !k.Contains("Shadow", StringComparison.OrdinalIgnoreCase)).ToList();

            var missing = stringDynKeys.Where(k => !enKeys.Contains(k)).ToList();
            Assert.Empty(missing);
        }
    }
}

