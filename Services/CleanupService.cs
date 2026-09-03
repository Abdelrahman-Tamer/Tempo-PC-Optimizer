using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Tempo.Services
{
    public class CleanupResult
    {
        public bool Success { get; set; }
        public string ActionName { get; set; } = string.Empty;
        public long ReclaimedBytes { get; set; }
        public double ReclaimedMb => Math.Round(ReclaimedBytes / (1024.0 * 1024.0), 2);
        public int DeletedFilesCount { get; set; }
        public int SkippedFilesCount { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? Warning { get; set; }
    }

    public class RecycleBinInfo
    {
        public long ItemCount { get; set; }
        public long TotalSizeBytes { get; set; }
        public double TotalSizeMb => Math.Round(TotalSizeBytes / (1024.0 * 1024.0), 2);
    }

    public class CleanupService
    {
        [DllImport("psapi.dll", SetLastError = true)]
        public static extern bool EmptyWorkingSet(IntPtr hProcess);

        public static bool IsSafePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            try
            {
                string fullPath = Path.GetFullPath(path).TrimEnd('\\');

                // Protected system and user directory roots that must NEVER be deleted
                var forbiddenRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    Path.GetPathRoot(fullPath) ?? "",
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32"),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                    Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                    AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\')
                };

                if (forbiddenRoots.Contains(fullPath)) return false;

                // Forbidden source / project extensions
                string ext = Path.GetExtension(fullPath);
                var forbiddenExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    ".cs", ".xaml", ".csproj", ".sln", ".config", ".cpp", ".h", ".py", ".java", ".go", ".rs"
                };

                // Only disallow source code files outside pure cache and temp folders
                if (forbiddenExts.Contains(ext) &&
                    !fullPath.Contains("Cache", StringComparison.OrdinalIgnoreCase) &&
                    !fullPath.Contains("Temp", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;

            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        // Win32 Shell Recycle Bin APIs
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct SHQUERYRBINFO
        {
            public int cbSize;
            public long i64Size;
            public long i64NumItems;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern int SHQueryRecycleBin(string pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

        private const uint SHERB_NOCONFIRMATION = 0x00000001;
        private const uint SHERB_NOPROGRESSUI   = 0x00000002;
        private const uint SHERB_NOSOUND        = 0x00000004;

        // Exactly 15 Protected System & Critical Processes
        public static readonly HashSet<string> ExcludedProcessNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "System",               // 1. Core NT Kernel
            "Idle",                 // 2. System Idle Process
            "Registry",             // 3. Windows Kernel Registry
            "Secure System",        // 4. Virtualization-based Security (VBS)
            "smss",                 // 5. Session Manager Subsystem
            "csrss",                // 6. Client/Server Runtime
            "wininit",              // 7. Windows Initialization
            "winlogon",             // 8. Windows Logon
            "services",             // 9. Service Control Manager
            "lsass",                // 10. Local Security Authority
            "dwm",                  // 11. Desktop Window Manager (Prevents screen flicker)
            "fontdrvhost",          // 12. User-mode Font Driver Host
            "Memory Compression",   // 13. Windows 11 Compressed Store
            "audiodg",              // 14. Windows Audio Device Graph (Prevents audio crackle)
            "Zenith"                // 15. The Zenith App itself
        };

        private readonly string _logFilePath;

        public CleanupService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string logDir = Path.Combine(appData, "Tempo", "logs");
            if (!Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
            }
            _logFilePath = Path.Combine(logDir, "app.log");
        }

        public void Log(string message, string level = "INFO")
        {
            try
            {
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";
                File.AppendAllText(_logFilePath, line + Environment.NewLine);
            }
            catch { }
        }

        public CleanupResult OptimizeRamWorkingSets() => TurboRamBoost();

        public CleanupResult TurboRamBoost()
        {
            var result = new CleanupResult { ActionName = "Turbo RAM Boost" };
            Log($"Starting Turbo RAM Boost (Protected list contains {ExcludedProcessNames.Count} system processes)...");

            var memBefore = new MEMORYSTATUSEX();
            GlobalMemoryStatusEx(memBefore);
            ulong availBefore = memBefore.ullAvailPhys;

            int processedCount = 0;
            int skippedCount = 0;

            Process[] processes = Process.GetProcesses();
            foreach (Process process in processes)
            {
                try
                {
                    string pName = process.ProcessName;
                    int pid = process.Id;

                    if (pid <= 4 || ExcludedProcessNames.Contains(pName))
                    {
                        skippedCount++;
                        continue;
                    }

                    if (EmptyWorkingSet(process.Handle))
                    {
                        processedCount++;
                    }
                    else
                    {
                        skippedCount++;
                    }
                }
                catch
                {
                    skippedCount++;
                }
                finally
                {
                    process.Dispose();
                }
            }

            System.Threading.Thread.Sleep(150);

            var memAfter = new MEMORYSTATUSEX();
            GlobalMemoryStatusEx(memAfter);
            ulong availAfter = memAfter.ullAvailPhys;

            long freedBytes = 0;
            if (availAfter > availBefore)
            {
                freedBytes = (long)(availAfter - availBefore);
            }

            result.Success = true;
            result.ReclaimedBytes = freedBytes;
            result.DeletedFilesCount = processedCount;
            result.SkippedFilesCount = skippedCount;
            result.Message = (LocalizationManager.CurrentLanguage == "ar")
                ? $"تم تفريغ الذاكرة لـ {processedCount} عملية بنجاح. تم تحرير \u200E{result.ReclaimedMb:F1} MB\u200E من الرام."
                : $"Trimmed working memory for {processedCount} processes. Reclaimed \u200E{result.ReclaimedMb:F1} MB\u200E of RAM.";
            Log($"Turbo RAM Boost completed: Reclaimed {result.ReclaimedMb:F1} MB across {processedCount} processes.");

            return result;
        }

        public CleanupResult QuickCleanTemp()
        {
            var result = new CleanupResult { ActionName = "Quick Clean (Temp & Prefetch)" };
            Log("Starting Quick Clean (Temp & Prefetch)...");

            var directoriesToClean = new List<(string path, bool checkDate)>
            {
                (Path.GetTempPath(), false),
                (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"), false),
                (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch"), true)
            };

            long totalFreedBytes = 0;
            int deletedFiles = 0;
            int skippedFiles = 0;
            DateTime prefetchCutoff = DateTime.Now.AddDays(-7);

            foreach (var (dirPath, checkDate) in directoriesToClean)
            {
                if (!Directory.Exists(dirPath))
                    continue;

                DirectoryInfo dirInfo = new DirectoryInfo(dirPath);
                FileInfo[] files;
                try
                {
                    files = dirInfo.GetFiles("*", SearchOption.TopDirectoryOnly);
                }
                catch
                {
                    continue;
                }

                foreach (FileInfo file in files)
                {
                    try
                    {
                        if (checkDate && file.LastWriteTime > prefetchCutoff)
                        {
                            skippedFiles++;
                            continue;
                        }

                        if (!IsSafePath(file.FullName))
                        {
                            skippedFiles++;
                            Log($"[SKIP - PROTECTED] {file.FullName}");
                            continue;
                        }

                        long size = file.Length;
                        file.Delete();
                        totalFreedBytes += size;
                        deletedFiles++;
                    }
                    catch
                    {
                        skippedFiles++;
                    }
                }

                if (dirPath.Equals(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        DirectoryInfo[] subDirs = dirInfo.GetDirectories();
                        foreach (DirectoryInfo subDir in subDirs)
                        {
                            try 
                            { 
                                if (IsSafePath(subDir.FullName))
                                {
                                    subDir.Delete(true); 
                                }
                            } 
                            catch { }
                        }
                    }
                    catch { }
                }
            }

            result.Success = true;
            result.ReclaimedBytes = totalFreedBytes;
            result.DeletedFilesCount = deletedFiles;
            result.SkippedFilesCount = skippedFiles;
            result.Message = (LocalizationManager.CurrentLanguage == "ar")
                ? $"تم حذف {deletedFiles} ملفاً مؤقتاً، وتحرير \u200E{result.ReclaimedMb:F1} MB\u200E بنجاح."
                : $"Deleted {deletedFiles} temporary files, reclaimed \u200E{result.ReclaimedMb:F1} MB\u200E.";
            Log($"Quick Clean completed: Deleted {deletedFiles} files, freed {result.ReclaimedMb:F1} MB.");

            return result;
        }

        public RecycleBinInfo QueryRecycleBin()
        {
            var info = new RecycleBinInfo();
            try
            {
                var rb = new SHQUERYRBINFO();
                rb.cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO));
                int hr = SHQueryRecycleBin(string.Empty, ref rb);
                if (hr == 0)
                {
                    info.ItemCount = rb.i64NumItems;
                    info.TotalSizeBytes = rb.i64Size;
                }
            }
            catch { }
            return info;
        }

        public CleanupResult EmptyRecycleBin()
        {
            var result = new CleanupResult { ActionName = "Recycle Bin Cleanup" };
            try
            {
                var before = QueryRecycleBin();
                if (before.ItemCount == 0)
                {
                    result.Success = true;
                    result.Message = (LocalizationManager.CurrentLanguage == "ar")
                        ? "سلة المحذوفات فارغة بالفعل (0 عنصر)."
                        : "Recycle Bin is already empty (0 items).";
                    return result;
                }

                uint flags = SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND;
                int hr = SHEmptyRecycleBin(IntPtr.Zero, null, flags);

                // Verify actual state after call
                var after = QueryRecycleBin();

                if (after.ItemCount == 0)
                {
                    result.Success = true;
                    result.ReclaimedBytes = before.TotalSizeBytes;
                    result.DeletedFilesCount = (int)before.ItemCount;
                    result.Message = (LocalizationManager.CurrentLanguage == "ar")
                        ? $"تم تفريغ سلة المحذوفات بنجاح. تم تحرير \u200E{before.TotalSizeMb:F1} MB\u200E وحذف {before.ItemCount} عنصر."
                        : $"Recycle Bin emptied successfully. Reclaimed \u200E{before.TotalSizeMb:F1} MB\u200E ({before.ItemCount:N0} items deleted).";
                    Log($"Recycle bin emptied successfully: Reclaimed {before.TotalSizeMb:F1} MB across {before.ItemCount} items.");
                }
                else if (after.ItemCount < before.ItemCount)
                {
                    long deletedCount = before.ItemCount - after.ItemCount;
                    long freedBytes = Math.Max(0, before.TotalSizeBytes - after.TotalSizeBytes);
                    double freedMb = Math.Round(freedBytes / (1024.0 * 1024.0), 2);

                    result.Success = false; // Partial failure
                    result.ReclaimedBytes = freedBytes;
                    result.DeletedFilesCount = (int)deletedCount;
                    result.SkippedFilesCount = (int)after.ItemCount;
                    result.Warning = (LocalizationManager.CurrentLanguage == "ar")
                        ? "فشل جزئي في تفريغ سلة المحذوفات"
                        : "Partial Recycle Bin cleanup";
                    result.Message = (LocalizationManager.CurrentLanguage == "ar")
                        ? $"تم حذف {deletedCount} عنصر فقط (\u200E{freedMb:F1} MB\u200E). تعذر حذف {after.ItemCount} عنصر لوجود ملفات مقفلة أو قيد الاستخدام."
                        : $"Deleted {deletedCount} items (\u200E{freedMb:F1} MB\u200E). Unable to delete {after.ItemCount} locked files.";
                    Log($"Recycle bin partial cleanup: Deleted {deletedCount}, remaining {after.ItemCount}.", "WARN");
                }
                else
                {
                    result.Success = false;
                    result.Message = hr != 0
                        ? ((LocalizationManager.CurrentLanguage == "ar")
                            ? $"تعذر تفريغ سلة المحذوفات (رمز الخطأ: 0x{hr:X8}). قد تكون الملفات قيد الاستخدام بواسطة تطبيق آخر."
                            : $"Unable to empty Recycle Bin (Error code: 0x{hr:X8}). Files may be locked by another application.")
                        : ((LocalizationManager.CurrentLanguage == "ar")
                            ? "تعذر تفريغ سلة المحذوفات: بقيت الملفات دون حذف."
                            : "Unable to empty Recycle Bin: files remained undeleted.");
                    Log($"Recycle bin empty failed. HRESULT=0x{hr:X8}", "ERROR");
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = (LocalizationManager.CurrentLanguage == "ar")
                    ? $"خطأ أثناء محاولة تفريغ سلة المحذوفات: {ex.Message}"
                    : $"Error emptying Recycle Bin: {ex.Message}";
                Log($"Recycle bin exception: {ex.Message}", "ERROR");
            }
            return result;
        }

        public CleanupResult BrowserCacheFlush()
        {
            var result = new CleanupResult { ActionName = "Browser Cache Flush" };
            Process[] chromeProcs = Process.GetProcessesByName("chrome");
            Process[] edgeProcs = Process.GetProcessesByName("msedge");

            var runningBrowsers = new List<string>();
            if (chromeProcs.Length > 0) runningBrowsers.Add("Google Chrome");
            if (edgeProcs.Length > 0) runningBrowsers.Add("Microsoft Edge");

            foreach (var p in chromeProcs) p.Dispose();
            foreach (var p in edgeProcs) p.Dispose();

            if (runningBrowsers.Count > 0)
            {
                result.Success = false;
                string browserNames = string.Join(LocalizationManager.CurrentLanguage == "ar" ? " و " : " & ", runningBrowsers);
                result.Warning = (LocalizationManager.CurrentLanguage == "ar")
                    ? $"المتصفح قيد التشغيل: ({browserNames})."
                    : $"Browser is running: ({browserNames}).";
                result.Message = (LocalizationManager.CurrentLanguage == "ar")
                    ? $"يرجى إغلاق {browserNames} يدوياً أولاً، ثم إعادة المحاولة لإتمام تنظيف الكاش بالكامل وبأمان."
                    : $"Please close {browserNames} manually first, then retry to safely flush cache.";
                return result;
            }

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var cacheDirs = new List<string>
            {
                Path.Combine(localAppData, @"Google\Chrome\User Data\Default\Cache"),
                Path.Combine(localAppData, @"Google\Chrome\User Data\Default\Code Cache"),
                Path.Combine(localAppData, @"Microsoft\Edge\User Data\Default\Cache"),
                Path.Combine(localAppData, @"Microsoft\Edge\User Data\Default\Code Cache")
            };

            long freedBytes = 0;
            int deletedFiles = 0;

            foreach (string cacheDir in cacheDirs)
            {
                if (!Directory.Exists(cacheDir)) continue;

                try
                {
                    DirectoryInfo dir = new DirectoryInfo(cacheDir);
                    FileInfo[] files = dir.GetFiles("*", SearchOption.AllDirectories);
                    foreach (FileInfo file in files)
                    {
                        try
                        {
                            long size = file.Length;
                            file.Delete();
                            freedBytes += size;
                            deletedFiles++;
                        }
                        catch { }
                    }
                }
                catch { }
            }

            result.Success = true;
            result.ReclaimedBytes = freedBytes;
            result.DeletedFilesCount = deletedFiles;
            result.Message = (LocalizationManager.CurrentLanguage == "ar")
                ? $"تم تنظيف كاش المتصفحات بنجاح: حذف {deletedFiles} ملف، وتحرير \u200E{result.ReclaimedMb:F1} MB\u200E."
                : $"Browser cache cleaned: deleted {deletedFiles} files, reclaimed \u200E{result.ReclaimedMb:F1} MB\u200E.";
            return result;
        }

        public CleanupResult DevCachesFlush()
        {
            var result = new CleanupResult { ActionName = "Dev Caches Flush" };
            long freedBytes = 0;
            int deletedFiles = 0;
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            // 1. npm cache (%LocalAppData%\npm-cache)
            string npmCacheDir = Path.Combine(localAppData, "npm-cache");
            if (Directory.Exists(npmCacheDir))
            {
                try
                {
                    DirectoryInfo dir = new DirectoryInfo(npmCacheDir);
                    foreach (FileInfo file in dir.GetFiles("*", SearchOption.AllDirectories))
                    {
                        try { long s = file.Length; file.Delete(); freedBytes += s; deletedFiles++; } catch { }
                    }
                }
                catch { }
            }

            // 2. pip cache (%LocalAppData%\pip\cache)
            string pipCacheDir = Path.Combine(localAppData, "pip", "cache");
            if (Directory.Exists(pipCacheDir))
            {
                try
                {
                    DirectoryInfo dir = new DirectoryInfo(pipCacheDir);
                    foreach (FileInfo file in dir.GetFiles("*", SearchOption.AllDirectories))
                    {
                        try { long s = file.Length; file.Delete(); freedBytes += s; deletedFiles++; } catch { }
                    }
                }
                catch { }
            }

            // 3. NuGet http-cache ONLY (%LocalAppData%\NuGet\v3-cache) - Never touches global-packages!
            string nugetHttpCache = Path.Combine(localAppData, "NuGet", "v3-cache");
            if (Directory.Exists(nugetHttpCache))
            {
                try
                {
                    DirectoryInfo dir = new DirectoryInfo(nugetHttpCache);
                    foreach (FileInfo file in dir.GetFiles("*", SearchOption.AllDirectories))
                    {
                        try { long s = file.Length; file.Delete(); freedBytes += s; deletedFiles++; } catch { }
                    }
                }
                catch { }
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = "nuget locals http-cache --clear",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(3000);
            }
            catch { }

            result.Success = true;
            result.ReclaimedBytes = freedBytes;
            result.DeletedFilesCount = deletedFiles;
            result.Message = (LocalizationManager.CurrentLanguage == "ar")
                ? $"تم تنظيف كاش المطورين بنجاح (npm, NuGet http-cache, pip): حذف {deletedFiles} ملف، وتحرير \u200E{result.ReclaimedMb:F1} MB\u200E."
                : $"Developer caches purged (npm, NuGet, pip): deleted {deletedFiles} files, reclaimed \u200E{result.ReclaimedMb:F1} MB\u200E.";
            return result;
        }

        public CleanupResult SsdReTrim(string driveLetter = "C")
        {
            var result = new CleanupResult { ActionName = "SSD Re-Trim" };
            driveLetter = (string.IsNullOrWhiteSpace(driveLetter) ? "C" : driveLetter).Trim().TrimEnd(':', '\\', '/').ToUpperInvariant();

            try
            {
                // Engine 1: Native Windows Defrag Retrim Engine (Direct volume RPC via defragsvc, bypasses WMI)
                string defragExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "defrag.exe");
                if (File.Exists(defragExe))
                {
                    var defragPsi = new ProcessStartInfo
                    {
                        FileName = defragExe,
                        Arguments = $"{driveLetter}: /L",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using (var proc = Process.Start(defragPsi))
                    {
                        if (proc != null)
                        {
                            string defragOut = proc.StandardOutput.ReadToEnd();
                            string defragErr = proc.StandardError.ReadToEnd();
                            proc.WaitForExit();

                            if (proc.ExitCode == 0)
                            {
                                result.Success = true;
                                result.Message = (LocalizationManager.CurrentLanguage == "ar")
                                    ? $"تم تنشيط وإعادة تهيئة خلايا التخزين الحرة على القرص {driveLetter}: بنجاح عبر TRIM."
                                    : $"SSD TRIM optimization completed on drive {driveLetter}: successfully.";
                                return result;
                            }
                        }
                    }
                }

                // Engine 2: PowerShell Storage Provider Fallback
                var psPsi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"Optimize-Volume -DriveLetter {driveLetter} -ReTrim -Verbose 2>&1\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var psProc = Process.Start(psPsi))
                {
                    if (psProc != null)
                    {
                        string psOut = psProc.StandardOutput.ReadToEnd();
                        string psErr = psProc.StandardError.ReadToEnd();
                        psProc.WaitForExit();

                        if (psProc.ExitCode == 0)
                        {
                            result.Success = true;
                            result.Message = (LocalizationManager.CurrentLanguage == "ar")
                                    ? $"تم تنشيط وإعادة تهيئة خلايا التخزين الحرة على القرص {driveLetter}: بنجاح عبر TRIM."
                                    : $"SSD TRIM optimization completed on drive {driveLetter}: successfully.";
                            return result;
                        }
                        else
                        {
                            string combinedErr = !string.IsNullOrWhiteSpace(psErr) ? psErr : psOut;
                            if (combinedErr.Contains("40001") || combinedErr.Contains("Access denied", StringComparison.OrdinalIgnoreCase) || combinedErr.Contains("0x89000024"))
                            {
                                result.Success = false;
                                result.Message = (LocalizationManager.CurrentLanguage == "ar")
                                    ? $"يتطلب تشغيل TRIM على القرص {driveLetter}: تشغيل التطبيق بصلاحيات مسؤول النظام (Administrator)."
                                    : $"Running TRIM on drive {driveLetter}: requires Administrator privileges.";
                            }
                            else
                            {
                                result.Success = false;
                                result.Message = (LocalizationManager.CurrentLanguage == "ar")
                                    ? $"تعذر إكمال TRIM على القرص {driveLetter}: {combinedErr.Trim()}"
                                    : $"Unable to complete TRIM on drive {driveLetter}: {combinedErr.Trim()}";
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = (LocalizationManager.CurrentLanguage == "ar")
                    ? $"خطأ أثناء تشغيل TRIM: {ex.Message}"
                    : $"Error executing TRIM: {ex.Message}";
            }

            return result;
        }

        public CleanupScanSummary ScanAllCaches()
        {
            var summary = new CleanupScanSummary();

            // 1. Temp Files Scan
            try
            {
                var dirs = new[]
                {
                    Path.GetTempPath(),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp")
                };

                foreach (var dir in dirs)
                {
                    if (!Directory.Exists(dir)) continue;
                    var di = new DirectoryInfo(dir);
                    try
                    {
                        foreach (var fi in di.GetFiles("*", SearchOption.TopDirectoryOnly))
                        {
                            try
                            {
                                summary.TempBytes += fi.Length;
                                summary.TempFiles++;
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
            catch { }

            // 2. Recycle Bin Scan
            try
            {
                var rb = QueryRecycleBin();
                summary.RecycleBinBytes = rb.TotalSizeBytes;
                summary.RecycleBinItems = rb.ItemCount;
            }
            catch { }

            // 3. Browser Cache Scan
            try
            {
                string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var browserDirs = new[]
                {
                    Path.Combine(localApp, @"Google\Chrome\User Data\Default\Cache"),
                    Path.Combine(localApp, @"Google\Chrome\User Data\Default\Code Cache"),
                    Path.Combine(localApp, @"Microsoft\Edge\User Data\Default\Cache"),
                    Path.Combine(localApp, @"Microsoft\Edge\User Data\Default\Code Cache")
                };

                foreach (var bd in browserDirs)
                {
                    if (!Directory.Exists(bd)) continue;
                    var di = new DirectoryInfo(bd);
                    try
                    {
                        foreach (var fi in di.GetFiles("*", SearchOption.AllDirectories))
                        {
                            try
                            {
                                summary.BrowserCacheBytes += fi.Length;
                                summary.BrowserCacheFiles++;
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
            catch { }

            // 4. Dev Cache Scan
            try
            {
                string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var devDirs = new[]
                {
                    Path.Combine(localApp, "npm-cache"),
                    Path.Combine(localApp, @"pip\cache"),
                    Path.Combine(localApp, @"NuGet\v3-cache")
                };

                foreach (var dd in devDirs)
                {
                    if (!Directory.Exists(dd)) continue;
                    var di = new DirectoryInfo(dd);
                    try
                    {
                        foreach (var fi in di.GetFiles("*", SearchOption.AllDirectories))
                        {
                            try
                            {
                                summary.DevCacheBytes += fi.Length;
                                summary.DevCacheFiles++;
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return summary;
        }
    }

    public class CleanupScanSummary
    {
        public long TempBytes { get; set; }
        public int TempFiles { get; set; }
        public long RecycleBinBytes { get; set; }
        public long RecycleBinItems { get; set; }
        public long BrowserCacheBytes { get; set; }
        public int BrowserCacheFiles { get; set; }
        public long DevCacheBytes { get; set; }
        public int DevCacheFiles { get; set; }

        public double TempMb => Math.Round(TempBytes / (1024.0 * 1024.0), 1);
        public double RecycleBinMb => Math.Round(RecycleBinBytes / (1024.0 * 1024.0), 1);
        public double BrowserCacheMb => Math.Round(BrowserCacheBytes / (1024.0 * 1024.0), 1);
        public double DevCacheMb => Math.Round(DevCacheBytes / (1024.0 * 1024.0), 1);
        public double TotalMb => Math.Round((TempBytes + RecycleBinBytes + BrowserCacheBytes + DevCacheBytes) / (1024.0 * 1024.0), 1);
        public int TotalItems => TempFiles + (int)RecycleBinItems + BrowserCacheFiles + DevCacheFiles;
    }
}
