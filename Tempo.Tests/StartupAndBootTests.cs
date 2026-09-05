using System;
using System.Collections.Generic;
using Tempo.Services;
using Xunit;

namespace Tempo.Tests
{
    public class StartupAndBootTests
    {
        [Fact]
        public void StartupGuard_RejectsSystemManagedEntries()
        {
            var service = new HardwareMonitorService();
            var app = new StartupAppItem
            {
                Name = "WindowsSecurityHealth",
                Command = "SecurityHealthSystray.exe",
                Location = "HKCU",
                IsSystemManaged = true,
                IsEnabled = true
            };

            bool result = service.ToggleStartupApp(app);
            Assert.False(result);
            Assert.True(app.IsEnabled); // State should not change
        }

        [Fact]
        public void ExcludedProcessNames_HasExactly37Entries()
        {
            Assert.Equal(37, CleanupService.ExcludedProcessNames.Count);
            Assert.Contains("Tempo", CleanupService.ExcludedProcessNames);
            Assert.Contains("RuntimeBroker", CleanupService.ExcludedProcessNames);
            Assert.Contains("explorer", CleanupService.ExcludedProcessNames);
            Assert.Contains("svchost", CleanupService.ExcludedProcessNames);
            Assert.Contains("dwm", CleanupService.ExcludedProcessNames);
            Assert.Contains("MsMpEng", CleanupService.ExcludedProcessNames);
        }

        [Fact]
        public void ProtectedSystemProcesses_HasExactly58Entries()
        {
            Assert.Equal(58, HardwareMonitorService.ProtectedSystemProcesses.Count);
            Assert.Contains("system", HardwareMonitorService.ProtectedSystemProcesses);
            Assert.Contains("smss", HardwareMonitorService.ProtectedSystemProcesses);
            Assert.Contains("csrss", HardwareMonitorService.ProtectedSystemProcesses);
            Assert.Contains("lsass", HardwareMonitorService.ProtectedSystemProcesses);
            Assert.Contains("services", HardwareMonitorService.ProtectedSystemProcesses);
            Assert.Contains("wininit", HardwareMonitorService.ProtectedSystemProcesses);
        }

        [Fact]
        public void BootIdStaleness_RejectsMismatchOrStaleTimestamps()
        {
            DateTime bootTime = DateTime.UtcNow.AddHours(-2);
            string currentBootId = bootTime.ToString("o");

            // Valid cache
            var validCache = new BootMeasureCache
            {
                Timestamp = DateTime.UtcNow.AddHours(-1),
                BootId = currentBootId,
                MainPathBootMs = 25000
            };
            bool isStaleValid = (validCache.Timestamp < DateTime.UtcNow.AddDays(-14))
                || (validCache.Timestamp < bootTime)
                || string.IsNullOrEmpty(validCache.BootId)
                || !string.Equals(validCache.BootId, currentBootId, StringComparison.OrdinalIgnoreCase);
            Assert.False(isStaleValid);

            // Mismatched BootId (from previous reboot)
            string oldBootId = DateTime.UtcNow.AddDays(-1).ToString("o");
            var mismatchedCache = new BootMeasureCache
            {
                Timestamp = DateTime.UtcNow.AddHours(-1),
                BootId = oldBootId,
                MainPathBootMs = 25000
            };
            bool isStaleMismatch = (mismatchedCache.Timestamp < DateTime.UtcNow.AddDays(-14))
                || (mismatchedCache.Timestamp < bootTime)
                || string.IsNullOrEmpty(mismatchedCache.BootId)
                || !string.Equals(mismatchedCache.BootId, currentBootId, StringComparison.OrdinalIgnoreCase);
            Assert.True(isStaleMismatch);

            // Stale timestamp (> 14 days)
            var oldTimestampCache = new BootMeasureCache
            {
                Timestamp = DateTime.UtcNow.AddDays(-15),
                BootId = currentBootId,
                MainPathBootMs = 25000
            };
            bool isStaleOldTimestamp = (oldTimestampCache.Timestamp < DateTime.UtcNow.AddDays(-14))
                || (oldTimestampCache.Timestamp < bootTime)
                || string.IsNullOrEmpty(oldTimestampCache.BootId)
                || !string.Equals(oldTimestampCache.BootId, currentBootId, StringComparison.OrdinalIgnoreCase);
            Assert.True(isStaleOldTimestamp);

            // Empty BootId
            var emptyBootIdCache = new BootMeasureCache
            {
                Timestamp = DateTime.UtcNow.AddHours(-1),
                BootId = "",
                MainPathBootMs = 25000
            };
            bool isStaleEmptyBootId = (emptyBootIdCache.Timestamp < DateTime.UtcNow.AddDays(-14))
                || (emptyBootIdCache.Timestamp < bootTime)
                || string.IsNullOrEmpty(emptyBootIdCache.BootId)
                || !string.Equals(emptyBootIdCache.BootId, currentBootId, StringComparison.OrdinalIgnoreCase);
            Assert.True(isStaleEmptyBootId);
        }

        [Fact]
        public void StartupAppItem_SystemManagedStatusLabels()
        {
            var item = new StartupAppItem
            {
                Name = "RuntimeBroker",
                IsSystemManaged = true,
                IsEnabled = true
            };

            Assert.Contains("System", item.StatusLabel);
            Assert.NotNull(item.StatusDotBrush);
        }
    }
}
