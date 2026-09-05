using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Tempo.Models;

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
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        public static bool IsSafePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            try
            {
                string fullPath = Path.GetFullPath(path).TrimEnd('\\', '/');

                // Check if path is a reparse point (symlink/junction) on disk
                if (File.Exists(fullPath) || Directory.Exists(fullPath))
                {
                    var attr = File.GetAttributes(fullPath);
                    if ((attr & FileAttributes.ReparsePoint) != 0)
                        return false;
                }

                // Root of drive must never be deleted (e.g. C:\)
                string? pathRoot = Path.GetPathRoot(fullPath);
                if (!string.IsNullOrEmpty(pathRoot) && fullPath.Equals(pathRoot.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                    return false;

                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

                // User profile root itself must never be deleted
                if (fullPath.Equals(userProfile.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                    return false;

                // Protected system and user directory roots that must NEVER be deleted
                var forbiddenRoots = new List<string>
                {
                    winDir,
                    Path.Combine(winDir, "System32"),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                    Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                    Path.Combine(userProfile, "Downloads"),
                    AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/')
                };

                foreach (var root in forbiddenRoots)
                {
                    if (string.IsNullOrWhiteSpace(root)) continue;
                    string cleanRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                    // Explicitly permit C:\Windows\Temp under Windows
                    if (cleanRoot.Equals(winDir.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                    {
                        string winTemp = Path.Combine(winDir, "Temp").TrimEnd('\\', '/');
                        if (fullPath.StartsWith(winTemp + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                            fullPath.Equals(winTemp, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }

                    if (fullPath.Equals(cleanRoot, StringComparison.OrdinalIgnoreCase) ||
                        fullPath.StartsWith(cleanRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }

                // Forbidden source / project extensions
                string ext = Path.GetExtension(fullPath);
                var forbiddenExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    ".cs", ".xaml", ".csproj", ".sln", ".config", ".cpp", ".h", ".py", ".java", ".go", ".rs"
                };

                if (forbiddenExts.Contains(ext))
                {
                    return false;
                }

                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException)
            {
                return false;
            }
        }

        public static IEnumerable<FileInfo> SafeEnumerateFiles(DirectoryInfo rootDir, bool apply24HourCutoff = false)
        {
            if (!rootDir.Exists) yield break;

            try
            {
                if ((rootDir.Attributes & FileAttributes.ReparsePoint) != 0)
                    yield break;
            }
            catch (Exception ex) when (ex is DirectoryNotFoundException or PathTooLongException or IOException or UnauthorizedAccessException or SecurityException)
            {
                yield break;
            }

            var cutoff = DateTime.Now.AddHours(-24);
            var stack = new Stack<DirectoryInfo>();
            stack.Push(rootDir);

            while (stack.Count > 0)
            {
                var currentDir = stack.Pop();
                try
                {
                    if ((currentDir.Attributes & FileAttributes.ReparsePoint) != 0)
                        continue;
                }
                catch (Exception ex) when (ex is DirectoryNotFoundException or PathTooLongException or IOException or UnauthorizedAccessException or SecurityException)
                {
                    continue;
                }

                FileInfo[] files;
                try
                {
                    files = currentDir.GetFiles();
                }
                catch (Exception ex) when (ex is DirectoryNotFoundException or PathTooLongException or IOException or UnauthorizedAccessException or SecurityException)
                {
                    continue;
                }

                foreach (var file in files)
                {
                    try
                    {
                        if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                            continue;

                        if (apply24HourCutoff)
                        {
                            if (file.LastWriteTime > cutoff || file.CreationTime > cutoff)
                                continue;
                        }
                    }
                    catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or PathTooLongException or IOException or UnauthorizedAccessException or SecurityException)
                    {
                        continue;
                    }

                    yield return file;
                }

                DirectoryInfo[] subDirs;
                try
                {
                    subDirs = currentDir.GetDirectories();
                }
                catch (Exception ex) when (ex is DirectoryNotFoundException or PathTooLongException or IOException or UnauthorizedAccessException or SecurityException)
                {
                    continue;
                }

                foreach (var sub in subDirs)
                {
                    try
                    {
                        if ((sub.Attributes & FileAttributes.ReparsePoint) == 0)
                        {
                            stack.Push(sub);
                        }
                    }
                    catch (Exception ex) when (ex is DirectoryNotFoundException or PathTooLongException or IOException or UnauthorizedAccessException or SecurityException)
                    {
                    }
                }
            }
        }

        public static void SafeDeleteEmptySubdirectories(DirectoryInfo dir)
        {
            try
            {
                if ((dir.Attributes & FileAttributes.ReparsePoint) != 0) return;
                foreach (var sub in dir.GetDirectories())
                {
                    try
                    {
                        if ((sub.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        SafeDeleteEmptySubdirectories(sub);
                        if (sub.GetFileSystemInfos().Length == 0 && IsSafePath(sub.FullName))
                        {
                            sub.Delete();
                        }
                    }
                    catch (Exception ex) when (ex is DirectoryNotFoundException or PathTooLongException or IOException or UnauthorizedAccessException or SecurityException)
                    {
                    }
                }
            }
            catch (Exception ex) when (ex is DirectoryNotFoundException or PathTooLongException or IOException or UnauthorizedAccessException or SecurityException)
            {
            }
        }

        private static bool GlobalMemoryStatusEx(MEMORYSTATUSEX lpBuffer) => NativeMethods.GlobalMemoryStatusEx(lpBuffer);

        // Win32 Shell Recycle Bin APIs
        [StructLayout(LayoutKind.Sequential)]
        private struct SHQUERYRBINFO
        {
            public int cbSize;
            public long i64Size;
            public long i64NumItems;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

        private const uint SHERB_NOCONFIRMATION = 0x00000001;
        private const uint SHERB_NOPROGRESSUI   = 0x00000002;
        private const uint SHERB_NOSOUND        = 0x00000004;

        // Comprehensive Protected System & Shell Processes Whitelist (Never evict working set of these)
        public static readonly HashSet<string> ExcludedProcessNames = new(StringComparer.OrdinalIgnoreCase)
        {
            // 1. Core NT Kernel & Subsystems
            "System",
            "Idle",
            "Registry",
            "Secure System",
            "smss",
            "csrss",
            "wininit",
            "winlogon",
            "services",
            "lsass",
            "Memory Compression",

            // 2. Desktop Window Manager, Graphics & Audio
            "dwm",
            "fontdrvhost",
            "audiodg",

            // 3. Windows Shell & Core Experience
            "explorer",
            "svchost",
            "sihost",
            "taskhostw",
            "RuntimeBroker",
            "ApplicationFrameHost",
            "ShellExperienceHost",
            "StartMenuExperienceHost",
            "SearchHost",
            "SearchIndexer",
            "SearchApp",
            "StartMenu",
            "SystemSettings",
            "TextInputHost",
            "ctfmon",
            "conhost",
            "spoolsv",
            "wlanext",

            // 4. Windows Security & Antivirus
            "MsMpEng",
            "SecurityHealthService",
            "SecurityHealthSystray",
            "NisSrv",

            // 5. Host Application
            "Tempo"
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

        private static readonly object _logLock = new();
        private const long MaxLogSizeBytes = 5 * 1024 * 1024; // 5 MB rotation threshold

        public void Log(string message, string level = "INFO")
        {
            try
            {
                lock (_logLock)
                {
                    var fi = new FileInfo(_logFilePath);
                    if (fi.Exists && fi.Length > MaxLogSizeBytes)
                    {
                        string oldLog = _logFilePath + ".old";
                        File.Move(_logFilePath, oldLog, overwrite: true);
                    }

                    string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";
                    File.AppendAllText(_logFilePath, line + Environment.NewLine);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
            }
        }

        public CleanupResult TurboRamBoost()
        {
            var result = new CleanupResult { ActionName = "Turbo RAM Boost" };
            Log($"Starting Turbo RAM Boost (Protected list contains {ExcludedProcessNames.Count} system processes)...");

            var memBefore = new MEMORYSTATUSEX();
            GlobalMemoryStatusEx(memBefore);
            ulong availBefore = memBefore.ullAvailPhys;

            int processedCount = 0;
            int skippedCount = 0;

            // Identify active foreground process to avoid lag/stutter in the user's active window
            uint foregroundPid = 0;
            try
            {
                IntPtr fgWnd = GetForegroundWindow();
                if (fgWnd != IntPtr.Zero)
                {
                    GetWindowThreadProcessId(fgWnd, out foregroundPid);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Log($"[Warning] Failed to identify foreground window: {ex.Message}");
            }

            Process[] processes = Array.Empty<Process>();
            try
            {
                processes = Process.GetProcesses();
                foreach (Process process in processes)
                {
                    try
                    {
                        string pName = process.ProcessName;
                        int pid = process.Id;

                        // Skip system kernel, protected processes, the active foreground window, and Tempo itself
                        if (pid <= 4 || pid == foregroundPid || ExcludedProcessNames.Contains(pName))
                        {
                            skippedCount++;
                            continue;
                        }

                        // Smart threshold: Only trim non-system processes using significant memory (> 40 MB)
                        // Capping at top 10 processes avoids hard page-fault storms and guarantees lightning fast completion (<150ms)
                        long workingSet = 0;
                        try { workingSet = process.WorkingSet64; } catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
                        if (workingSet < 40L * 1024 * 1024)
                        {
                            skippedCount++;
                            continue;
                        }

                        if (EmptyWorkingSet(process.Handle))
                        {
                            processedCount++;
                            if (processedCount >= 10)
                            {
                                break;
                            }
                        }
                        else
                        {
                            skippedCount++;
                        }
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                    {
                        skippedCount++;
                    }
                }
            }
            finally
            {
                foreach (var p in processes)
                {
                    try { p.Dispose(); } catch (Exception ex) when (ex is not OutOfMemoryException) { }
                }
            }

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
                ? $"تم تسريع الذاكرة: تحرير \u200E{result.ReclaimedMb:F1} MB\u200E"
                : $"RAM Boosted: \u200E{result.ReclaimedMb:F1} MB\u200E freed";
            Log($"Turbo RAM Boost completed: Reclaimed {result.ReclaimedMb:F1} MB across {processedCount} processes.");

            return result;
        }

        public CleanupResult QuickCleanTemp()
        {
            var result = new CleanupResult { ActionName = "Quick Clean (Temp)" };
            Log("Starting Quick Clean (Temp)...");

            var directoriesToClean = new List<string>
            {
                Path.GetTempPath(),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp")
            };

            long totalFreedBytes = 0;
            int deletedFiles = 0;
            int skippedFiles = 0;

            foreach (var dirPath in directoriesToClean)
            {
                if (!Directory.Exists(dirPath))
                    continue;

                DirectoryInfo dirInfo = new DirectoryInfo(dirPath);

                foreach (FileInfo file in SafeEnumerateFiles(dirInfo, apply24HourCutoff: true))
                {
                    try
                    {
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
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
                    {
                        skippedFiles++;
                    }
                }

                // Safely clean empty subdirectories without touching or traversing reparse points
                SafeDeleteEmptySubdirectories(dirInfo);
            }

            result.Success = true;
            result.ReclaimedBytes = totalFreedBytes;
            result.DeletedFilesCount = deletedFiles;
            result.SkippedFilesCount = skippedFiles;
            result.Message = (LocalizationManager.CurrentLanguage == "ar")
                ? $"تم تنظيف {deletedFiles} ملف مؤقت (\u200E{result.ReclaimedMb:F1} MB\u200E)"
                : $"Cleaned {deletedFiles} temp files (\u200E{result.ReclaimedMb:F1} MB\u200E)";
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
                int hr = SHQueryRecycleBin(null, ref rb);
                if (hr != 0)
                {
                    hr = SHQueryRecycleBin(@"C:\", ref rb);
                }
                if (hr == 0)
                {
                    info.ItemCount = rb.i64NumItems;
                    info.TotalSizeBytes = rb.i64Size;
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Log($"[Warning] QueryRecycleBin failed: {ex.Message}");
            }
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
                        ? "سلة المحذوفات فارغة بالفعل"
                        : "Recycle Bin is already empty";
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
                        ? $"تم إفراغ سلة المحذوفات: توفير \u200E{before.TotalSizeMb:F1} MB\u200E"
                        : $"Recycle Bin emptied: \u200E{before.TotalSizeMb:F1} MB\u200E freed";
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
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                result.Success = false;
                result.Message = (LocalizationManager.CurrentLanguage == "ar")
                    ? $"خطأ أثناء محاولة تفريغ سلة المحذوفات: {ex.Message}"
                    : $"Error emptying Recycle Bin: {ex.Message}";
                Log($"Recycle bin exception: {ex.Message}", "ERROR");
            }
            return result;
        }

        public static List<string> GetBrowserCacheDirectories()
        {
            var dirs = new List<string>();
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            // Chrome Profiles (Default, Profile 1, Profile 2, etc.)
            AddChromiumProfiles(Path.Combine(localAppData, "Google", "Chrome", "User Data"), dirs);

            // Edge Profiles
            AddChromiumProfiles(Path.Combine(localAppData, "Microsoft", "Edge", "User Data"), dirs);

            // Brave Profiles
            AddChromiumProfiles(Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data"), dirs);

            // Firefox Profiles
            string firefoxProfiles = Path.Combine(appData, "Mozilla", "Firefox", "Profiles");
            if (Directory.Exists(firefoxProfiles))
            {
                try
                {
                    foreach (var pDir in Directory.GetDirectories(firefoxProfiles))
                    {
                        string c2 = Path.Combine(pDir, "cache2", "entries");
                        if (Directory.Exists(c2)) dirs.Add(c2);
                        string sc = Path.Combine(pDir, "startupCache");
                        if (Directory.Exists(sc)) dirs.Add(sc);
                    }
                }
                catch (Exception ex) when (ex is DirectoryNotFoundException or PathTooLongException or IOException or UnauthorizedAccessException or SecurityException)
                {
                }
            }

            return dirs;
        }

        private static void AddChromiumProfiles(string userDataDir, List<string> dirs)
        {
            if (!Directory.Exists(userDataDir)) return;
            try
            {
                foreach (var sub in Directory.GetDirectories(userDataDir))
                {
                    string name = Path.GetFileName(sub);
                    if (name.Equals("Default", StringComparison.OrdinalIgnoreCase) ||
                        name.StartsWith("Profile", StringComparison.OrdinalIgnoreCase))
                    {
                        string c = Path.Combine(sub, "Cache");
                        if (Directory.Exists(c)) dirs.Add(c);
                        string cc = Path.Combine(sub, "Code Cache");
                        if (Directory.Exists(cc)) dirs.Add(cc);
                    }
                }
            }
            catch (Exception ex) when (ex is DirectoryNotFoundException or PathTooLongException or IOException or UnauthorizedAccessException or SecurityException)
            {
            }
        }

        public CleanupResult BrowserCacheFlush()
        {
            var result = new CleanupResult { ActionName = "Browser Cache Flush" };

            var runningBrowsers = new List<string>();
            CheckRunningBrowser("chrome", "Google Chrome", runningBrowsers);
            CheckRunningBrowser("msedge", "Microsoft Edge", runningBrowsers);
            CheckRunningBrowser("brave", "Brave", runningBrowsers);
            CheckRunningBrowser("firefox", "Mozilla Firefox", runningBrowsers);

            if (runningBrowsers.Count > 0)
            {
                result.Success = false;
                string browserNames = string.Join(LocalizationManager.CurrentLanguage == "ar" ? " و " : " & ", runningBrowsers);
                result.Warning = (LocalizationManager.CurrentLanguage == "ar")
                    ? $"المتصفح قيد التشغيل: ({browserNames})."
                    : $"Browser is running: ({browserNames}).";
                result.Message = (LocalizationManager.CurrentLanguage == "ar")
                    ? $"يرجى إغلاق {browserNames} أولاً"
                    : $"Please close {browserNames} first";
                return result;
            }

            var cacheDirs = GetBrowserCacheDirectories();
            long freedBytes = 0;
            int deletedFiles = 0;

            foreach (string cacheDir in cacheDirs)
            {
                if (!Directory.Exists(cacheDir)) continue;

                try
                {
                    DirectoryInfo dir = new DirectoryInfo(cacheDir);
                    foreach (FileInfo file in SafeEnumerateFiles(dir, apply24HourCutoff: false))
                    {
                        try
                        {
                            if (!IsSafePath(file.FullName)) continue;
                            long size = file.Length;
                            file.Delete();
                            freedBytes += size;
                            deletedFiles++;
                        }
                        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or PathTooLongException or IOException or UnauthorizedAccessException or SecurityException)
                        {
                        }
                    }
                    SafeDeleteEmptySubdirectories(dir);
                }
                catch (Exception ex) when (ex is DirectoryNotFoundException or PathTooLongException or IOException or UnauthorizedAccessException or SecurityException)
                {
                }
            }

            result.ReclaimedBytes = freedBytes;
            result.DeletedFilesCount = deletedFiles;
            result.Success = true;
            result.Message = (LocalizationManager.CurrentLanguage == "ar")
                ? $"تم تنظيف كاش المتصفح (\u200E{result.ReclaimedMb:F1} MB\u200E)"
                : $"Browser cache cleaned (\u200E{result.ReclaimedMb:F1} MB\u200E)";
            return result;
        }

        private static void CheckRunningBrowser(string procName, string friendlyName, List<string> running)
        {
            try
            {
                var procs = Process.GetProcessesByName(procName);
                if (procs.Length > 0)
                {
                    running.Add(friendlyName);
                    foreach (var p in procs) p.Dispose();
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
            }
        }

        public CleanupResult DevCachesFlush()
        {
            var result = new CleanupResult { ActionName = "Dev Caches Flush" };
            long freedBytes = 0;
            int deletedFiles = 0;
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            var devDirs = new[]
            {
                Path.Combine(localAppData, "npm-cache"),
                Path.Combine(localAppData, "pip", "cache"),
                Path.Combine(localAppData, "NuGet", "v3-cache")
            };

            foreach (var devDir in devDirs)
            {
                if (!Directory.Exists(devDir)) continue;
                try
                {
                    DirectoryInfo dir = new DirectoryInfo(devDir);
                    foreach (FileInfo file in SafeEnumerateFiles(dir, apply24HourCutoff: false))
                    {
                        try
                        {
                            if (!IsSafePath(file.FullName)) continue;
                            long s = file.Length;
                            file.Delete();
                            freedBytes += s;
                            deletedFiles++;
                        }
                        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or PathTooLongException or IOException or UnauthorizedAccessException or SecurityException)
                        {
                        }
                    }
                    SafeDeleteEmptySubdirectories(dir);
                }
                catch (Exception ex) when (ex is DirectoryNotFoundException or PathTooLongException or IOException or UnauthorizedAccessException or SecurityException)
                {
                }
            }

            try
            {
                string? resolvedDotnet = ResolveSignedDotnet();
                if (!string.IsNullOrEmpty(resolvedDotnet))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = resolvedDotnet,
                        Arguments = "nuget locals http-cache --clear",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        if (!proc.WaitForExit(5000))
                        {
                            try { proc.Kill(entireProcessTree: true); } catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or IOException or UnauthorizedAccessException or SecurityException or CryptographicException or FormatException)
            {
            }

            result.Success = true;
            result.ReclaimedBytes = freedBytes;
            result.DeletedFilesCount = deletedFiles;
            result.Message = (LocalizationManager.CurrentLanguage == "ar")
                ? $"تم تنظيف كاش المطورين (\u200E{result.ReclaimedMb:F1} MB\u200E)"
                : $"Developer cache cleaned (\u200E{result.ReclaimedMb:F1} MB\u200E)";
            return result;
        }

        private static bool IsSuspiciousOrUserWritablePath(string path)
        {
            try
            {
                string fullPath = Path.GetFullPath(path);
                string temp = Path.GetTempPath();
                if (!string.IsNullOrEmpty(temp) && fullPath.StartsWith(Path.GetFullPath(temp), StringComparison.OrdinalIgnoreCase))
                    return true;

                string? envTemp = Environment.GetEnvironmentVariable("TEMP");
                if (!string.IsNullOrEmpty(envTemp) && fullPath.StartsWith(Path.GetFullPath(envTemp), StringComparison.OrdinalIgnoreCase))
                    return true;

                string? envTmp = Environment.GetEnvironmentVariable("TMP");
                if (!string.IsNullOrEmpty(envTmp) && fullPath.StartsWith(Path.GetFullPath(envTmp), StringComparison.OrdinalIgnoreCase))
                    return true;

                if (fullPath.IndexOf(@"\Temp\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    fullPath.IndexOf(@"\Tmp\", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                return false;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return true;
            }
        }

        private static string? ValidateSignedDotnetCandidate(string candidate)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(candidate) || !File.Exists(candidate))
                    return null;

                if (IsSuspiciousOrUserWritablePath(candidate))
                    return null;

                string fullPath = Path.GetFullPath(candidate);

#pragma warning disable SYSLIB0057
                using var rawCert = X509Certificate.CreateFromSignedFile(fullPath);
#pragma warning restore SYSLIB0057
                using var cert = new X509Certificate2(rawCert);

                bool hasMsSubjectOrIssuer =
                    (cert.Subject?.Contains("Microsoft Corporation", StringComparison.OrdinalIgnoreCase) == true) ||
                    (cert.Issuer?.Contains("Microsoft Corporation", StringComparison.OrdinalIgnoreCase) == true);

                if (!hasMsSubjectOrIssuer)
                    return null;

                using var chain = new X509Chain();
                chain.ChainPolicy.RevocationMode = X509RevocationMode.Offline;
                chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
                bool chainOk = chain.Build(cert);

                bool notTimeValid = false;
                foreach (var status in chain.ChainStatus)
                {
                    if (status.Status.HasFlag(X509ChainStatusFlags.NotTimeValid))
                    {
                        notTimeValid = true;
                        break;
                    }
                }
                if (notTimeValid)
                    return null;

                if (!chainOk)
                {
                    bool hasMsInChain = false;
                    foreach (var elem in chain.ChainElements)
                    {
                        if (elem.Certificate.Subject.Contains("Microsoft Corporation", StringComparison.OrdinalIgnoreCase) ||
                            elem.Certificate.Issuer.Contains("Microsoft Corporation", StringComparison.OrdinalIgnoreCase))
                        {
                            hasMsInChain = true;
                            break;
                        }
                    }
                    if (!hasMsInChain)
                        return null;
                }

                var fvi = FileVersionInfo.GetVersionInfo(fullPath);
                if (!string.Equals(fvi.CompanyName, "Microsoft Corporation", StringComparison.OrdinalIgnoreCase))
                    return null;

                return fullPath;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return null;
            }
        }

        private static string? ResolveSignedDotnet()
        {
            var primaryCandidates = new List<string>();

            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrEmpty(pf))
                primaryCandidates.Add(Path.Combine(pf, "dotnet", "dotnet.exe"));

            string pfx86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrEmpty(pfx86))
                primaryCandidates.Add(Path.Combine(pfx86, "dotnet", "dotnet.exe"));

            string? dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            if (!string.IsNullOrEmpty(dotnetRoot))
                primaryCandidates.Add(Path.Combine(dotnetRoot, "dotnet.exe"));

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(localAppData))
                primaryCandidates.Add(Path.Combine(localAppData, "Microsoft", "dotnet", "dotnet.exe"));

            foreach (var candidate in primaryCandidates)
            {
                string? valid = ValidateSignedDotnetCandidate(candidate);
                if (valid != null) return valid;
            }

            // where.exe via Path.Combine(SystemDirectory, "where.exe") LAST
            try
            {
                string whereExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "where.exe");
                if (File.Exists(whereExe))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = whereExe,
                        Arguments = "dotnet",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var proc = Process.Start(psi);
                    if (proc != null && proc.WaitForExit(1000))
                    {
                        string output = proc.StandardOutput.ReadToEnd();
                        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var line in lines)
                        {
                            string candidate = line.Trim();
                            if (!string.IsNullOrEmpty(candidate) && candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            {
                                string? valid = ValidateSignedDotnetCandidate(candidate);
                                if (valid != null) return valid;
                            }
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
            }

            return null;
        }

        public CleanupResult SsdReTrim(string driveLetter = "C")
        {
            var result = new CleanupResult { ActionName = "SSD Re-Trim" };
            driveLetter = (string.IsNullOrWhiteSpace(driveLetter) ? "C" : driveLetter).Trim().TrimEnd(':', '\\', '/').ToUpperInvariant();

            if (driveLetter.Length != 1 || driveLetter[0] < 'A' || driveLetter[0] > 'Z')
            {
                result.Success = false;
                result.Message = (LocalizationManager.CurrentLanguage == "ar") ? "حرف القرص غير صالح." : "Invalid drive letter.";
                return result;
            }

            try
            {
                var dInfo = new DriveInfo(driveLetter);
                if (dInfo.DriveType != DriveType.Fixed)
                {
                    result.Success = false;
                    result.Message = (LocalizationManager.CurrentLanguage == "ar")
                        ? $"القرص {driveLetter}: ليس قرصاً ثابتاً."
                        : $"Drive {driveLetter}: is not a fixed drive.";
                    return result;
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                result.Success = false;
                result.Message = ex.Message;
                return result;
            }

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
                            var outTask = proc.StandardOutput.ReadToEndAsync();
                            var errTask = proc.StandardError.ReadToEndAsync();
                            bool exited = proc.WaitForExit(30000);
                            if (!exited)
                            {
                                try { proc.Kill(entireProcessTree: true); } catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
                            }
                            string defragOut = outTask.GetAwaiter().GetResult();
                            string defragErr = errTask.GetAwaiter().GetResult();

                            if (exited && proc.ExitCode == 0)
                            {
                                result.Success = true;
                                result.Message = (LocalizationManager.CurrentLanguage == "ar")
                                    ? $"تم تنشيط الـ SSD للقرص {driveLetter}: بنجاح"
                                    : $"SSD {driveLetter}: TRIM complete";
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
                        var psOutTask = psProc.StandardOutput.ReadToEndAsync();
                        var psErrTask = psProc.StandardError.ReadToEndAsync();
                        bool psExited = psProc.WaitForExit(30000);
                        if (!psExited)
                        {
                            try { psProc.Kill(entireProcessTree: true); } catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
                        }
                        string psOut = psOutTask.GetAwaiter().GetResult();
                        string psErr = psErrTask.GetAwaiter().GetResult();

                        if (psExited && psProc.ExitCode == 0)
                        {
                            result.Success = true;
                            result.Message = (LocalizationManager.CurrentLanguage == "ar")
                                    ? $"تم تنشيط الـ SSD للقرص {driveLetter}: بنجاح"
                                    : $"SSD {driveLetter}: TRIM complete";
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
            catch (Exception ex) when (ex is not OutOfMemoryException)
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

            // 1. Temp Files Scan (using same safe 24-hour cutoff as QuickCleanTemp)
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
                        foreach (var fi in SafeEnumerateFiles(di, apply24HourCutoff: true))
                        {
                            try
                            {
                                if (!IsSafePath(fi.FullName)) continue;
                                summary.TempBytes += fi.Length;
                                summary.TempFiles++;
                            }
                            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or PathTooLongException or IOException or UnauthorizedAccessException or SecurityException)
                            {
                            }
                        }
                    }
                    catch (Exception ex) when (ex is DirectoryNotFoundException or PathTooLongException or IOException or UnauthorizedAccessException or SecurityException)
                    {
                    }
                }
            }
            catch (Exception ex) when (ex is DirectoryNotFoundException or PathTooLongException or IOException or UnauthorizedAccessException or SecurityException)
            {
            }

            // 2. Recycle Bin Scan
            try
            {
                var rb = QueryRecycleBin();
                summary.RecycleBinBytes = rb.TotalSizeBytes;
                summary.RecycleBinItems = rb.ItemCount;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Log($"[Warning] Failed to scan Recycle Bin: {ex.Message}");
            }

            // 3. Browser Cache Scan (using SafeEnumerateFiles without symlink traversal)
            try
            {
                var browserDirs = GetBrowserCacheDirectories();

                foreach (var bd in browserDirs)
                {
                    if (!Directory.Exists(bd)) continue;
                    var di = new DirectoryInfo(bd);
                    try
                    {
                        foreach (var fi in SafeEnumerateFiles(di, apply24HourCutoff: false))
                        {
                            try
                            {
                                if (!IsSafePath(fi.FullName)) continue;
                                summary.BrowserCacheBytes += fi.Length;
                                summary.BrowserCacheFiles++;
                            }
                            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or PathTooLongException or IOException or UnauthorizedAccessException or SecurityException)
                            {
                            }
                        }
                    }
                    catch (Exception ex) when (ex is DirectoryNotFoundException or PathTooLongException or IOException or UnauthorizedAccessException or SecurityException)
                    {
                    }
                }
            }
            catch (Exception ex) when (ex is DirectoryNotFoundException or PathTooLongException or IOException or UnauthorizedAccessException or SecurityException)
            {
            }

            // 4. Dev Cache Scan (using SafeEnumerateFiles without symlink traversal)
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
                        foreach (var fi in SafeEnumerateFiles(di, apply24HourCutoff: false))
                        {
                            try
                            {
                                if (!IsSafePath(fi.FullName)) continue;
                                summary.DevCacheBytes += fi.Length;
                                summary.DevCacheFiles++;
                            }
                            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or PathTooLongException or IOException or UnauthorizedAccessException or SecurityException)
                            {
                            }
                        }
                    }
                    catch (Exception ex) when (ex is DirectoryNotFoundException or PathTooLongException or IOException or UnauthorizedAccessException or SecurityException)
                    {
                    }
                }
            }
            catch (Exception ex) when (ex is DirectoryNotFoundException or PathTooLongException or IOException or UnauthorizedAccessException or SecurityException)
            {
            }

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
