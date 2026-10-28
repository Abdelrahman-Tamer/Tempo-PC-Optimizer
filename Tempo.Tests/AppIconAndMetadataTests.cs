using System;
using System.IO;
using System.Windows;
using Tempo.Services;
using Xunit;

namespace Tempo.Tests
{
    public class AppIconAndMetadataTests
    {
        [Fact]
        public void TryResolveAppPathByName_ResolvesStandardToolsOrGracefullyReturnsNull()
        {
            // Null or empty returns null safely
            Assert.Null(HardwareMonitorService.TryResolveAppPathByName(null));
            Assert.Null(HardwareMonitorService.TryResolveAppPathByName("   "));
            Assert.Null(HardwareMonitorService.TryResolveAppPathByName("non_existent_app_12345xyz"));

            // System32 tools like cmd or powershell should resolve to existing files on Windows
            string? cmdPath = HardwareMonitorService.TryResolveAppPathByName("cmd");
            if (cmdPath != null)
            {
                Assert.True(File.Exists(cmdPath));
                Assert.EndsWith(".exe", cmdPath, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public void ExtractAppIcon_HandlesNullOrMissingPathsSafely()
        {
            // Should not throw on null or invalid path
            var icon1 = HardwareMonitorService.ExtractAppIcon(null);
            Assert.Null(icon1);

            var icon2 = HardwareMonitorService.ExtractAppIcon(@"C:\non_existent_folder_xyz\fake.exe");
            Assert.Null(icon2);
        }

        [Fact]
        public void GetCachedProcessMetadata_ReturnsValidDisplayNameAndCachesResult()
        {
            var meta1 = HardwareMonitorService.GetCachedProcessMetadata("chrome", null);
            Assert.NotNull(meta1.displayName);
            Assert.Equal("Google Chrome", meta1.displayName);

            var meta2 = HardwareMonitorService.GetCachedProcessMetadata("chrome", null);
            Assert.Equal(meta1.displayName, meta2.displayName);

            var metaVs = HardwareMonitorService.GetCachedProcessMetadata("code", null);
            Assert.NotNull(metaVs.displayName);
            Assert.Contains("Visual Studio Code", metaVs.displayName);
        }

        [Fact]
        public void RunningProcessItem_Monogram_ReturnsUppercaseInitial()
        {
            var item1 = new RunningProcessItem
            {
                DisplayName = "Google Chrome",
                MainProcessName = "chrome"
            };
            Assert.Equal("G", item1.Monogram);

            var item2 = new RunningProcessItem
            {
                DisplayName = "",
                MainProcessName = "spotify"
            };
            Assert.Equal("S", item2.Monogram);

            var item3 = new RunningProcessItem
            {
                DisplayName = "",
                MainProcessName = ""
            };
            Assert.Equal("A", item3.Monogram);
        }

        [Fact]
        public void StartupAppItem_IconVisibilityAndMonogram_BehaveCorrectly()
        {
            var app = new StartupAppItem
            {
                Name = "Discord",
                IconSource = null
            };

            Assert.Equal("D", app.Monogram);
            Assert.Equal(Visibility.Collapsed, app.HasIconVisibility);
            Assert.Equal(Visibility.Visible, app.FallbackIconVisibility);
        }

        [Fact]
        public void ProcessViewModel_PropertiesAndFormatting_BehaveCorrectly()
        {
            var vm = new ProcessViewModel
            {
                ProcessName = "msedge",
                DisplayName = "Microsoft Edge",
                RamMb = 512.4,
                IconSource = null
            };

            Assert.Equal("M", vm.Monogram);
            Assert.Equal(Visibility.Collapsed, vm.HasIconVisibility);
            Assert.Equal(Visibility.Visible, vm.FallbackIconVisibility);
            Assert.NotEmpty(vm.RamFormatted);
        }

        [Fact]
        public void HardwareMonitorService_ExtendedStats_ReturnMeaningfulStringsWithoutExceptions()
        {
            var service = new HardwareMonitorService();

            string cpu = service.GetCpuName();
            Assert.False(string.IsNullOrWhiteSpace(cpu));

            string device = service.GetDeviceModel();
            Assert.False(string.IsNullOrWhiteSpace(device));

            string ram = service.GetRamHardwareSpecs();
            Assert.False(string.IsNullOrWhiteSpace(ram));
            Assert.Contains("GB", ram);

            string uptime = service.GetSystemUptimeFormatted();
            Assert.False(string.IsNullOrWhiteSpace(uptime));

            var (hasBattery, powerStr) = service.GetPowerAndBatteryStatus();
            Assert.False(string.IsNullOrWhiteSpace(powerStr));
        }
    }
}
