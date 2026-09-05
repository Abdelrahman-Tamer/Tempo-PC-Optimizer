using System;
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
# Release v2.2.5
- High-contrast text update for WCAG 2.2 AA
- Excluded protected system services
- Added self-elevating TRIM handler
SHA256: 1234567890abcdef
https://example.com/download
";
            string formatted = MainWindow.FormatReleaseHighlights(markdown);

            Assert.Contains("High-contrast text update for WCAG 2.2 AA", formatted);
            Assert.Contains("Excluded protected system services", formatted);
            Assert.Contains("Added self-elevating TRIM handler", formatted);
            // Must exclude SHA256 and URLs
            Assert.DoesNotContain("SHA256", formatted);
            Assert.DoesNotContain("https://", formatted);
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
        public void UpdateInfo_CurrentVersion_IsVersion225()
        {
            var updateInfo = new UpdateInfo();
            Assert.Equal("2.2.5", updateInfo.CurrentVersion);
        }

        [Fact]
        public void UpdateService_GetCurrentVersion_Matches225()
        {
            var version = UpdateService.GetCurrentVersion();
            Assert.Equal(new Version(2, 2, 5), version);
        }
    }
}
