using System;
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
        public void FormatReleaseHighlights_ExtractsSnippetWhenNoBulletsPresent()
        {
            string headersAndUrlsOnly = "# Release v2.2.5\n## Security Updates\nhttps://github.com/repo/releases";
            string formatted = MainWindow.FormatReleaseHighlights(headersAndUrlsOnly);

            Assert.True(formatted.Contains("No release notes provided", StringComparison.OrdinalIgnoreCase) ||
                        formatted.Contains("لا توجد ملاحظات إصدار", StringComparison.OrdinalIgnoreCase));
            Assert.Contains("# Release v2.2.5", formatted);
        }

        [Fact]
        public void UpdateInfo_CurrentVersion_IsVersion226()
        {
            var updateInfo = new UpdateInfo();
            Assert.Equal("2.2.6", updateInfo.CurrentVersion);
        }

        [Fact]
        public void UpdateService_GetCurrentVersion_Matches226()
        {
            var version = UpdateService.GetCurrentVersion();
            Assert.Equal(new Version(2, 2, 6), version);
        }

        [Fact]
        public void CompanionBar_HorizontalWidth_IsConfiguredTo630()
        {
            var field = typeof(MainWindow).GetField("BarWidthH", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.NotNull(field);
            double width = (double)field.GetValue(null)!;
            Assert.Equal(630.0, width);
        }
    }
}
