using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Tempo.Models;

namespace Tempo.Services
{
    public enum UpdateInstallStatus
    {
        Success,
        UserCancelledUac,
        HashMismatch,
        FileNotFound,
        Failed
    }

    public class UpdateService
    {
        private const string GitHubRepo = "Abdelrahman-Tamer/Tempo-PC-Optimizer";
        private const string ReleasesApiUrl = "https://api.github.com/repos/" + GitHubRepo + "/releases/latest";
        private static readonly HttpClient _httpClient = new HttpClient();

        static UpdateService()
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Tempo-PC-Optimizer-Client");
            _httpClient.Timeout = TimeSpan.FromSeconds(25);
        }

        public static Version GetCurrentVersion()
        {
            try
            {
                var ver = Assembly.GetExecutingAssembly().GetName().Version;
                if (ver != null)
                {
                    return new Version(ver.Major, ver.Minor, Math.Max(0, ver.Build));
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
            }
            return new Version(2, 2, 5);
        }

        public async Task<UpdateInfo?> CheckForUpdatesAsync(bool force = false)
        {
            var settings = UpdateSettings.Load();

            // Caching check: Avoid flooding GitHub API (60 req/hr limit)
            if (!force && settings.LastCheckTimeUtc.HasValue)
            {
                var hoursSinceLastCheck = (DateTime.UtcNow - settings.LastCheckTimeUtc.Value).TotalHours;
                if (hoursSinceLastCheck < 6)
                {
                    return null; // Within cache period, skip check
                }
            }

            try
            {
                using var response = await _httpClient.GetAsync(ReleasesApiUrl).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                settings.LastCheckTimeUtc = DateTime.UtcNow;
                settings.Save();

                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string tagName = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? "" : "";
                string releaseName = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
                string releaseBody = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? "" : "";
                DateTime? publishedAt = null;
                if (root.TryGetProperty("published_at", out var pubProp) && pubProp.TryGetDateTime(out var dt))
                {
                    publishedAt = dt;
                }

                string cleanTag = tagName.Trim().TrimStart('v', 'V');
                if (!Version.TryParse(cleanTag, out var latestVersion))
                {
                    // If tag is 2.2, normalize to 2.2.0
                    if (Version.TryParse(cleanTag + ".0", out var normalizedVer))
                    {
                        latestVersion = normalizedVer;
                    }
                    else
                    {
                        return null;
                    }
                }

                var currentVersion = GetCurrentVersion();
                bool isNewer = latestVersion > currentVersion;

                // Check if user chose to skip this version
                bool isSkipped = !force && !string.IsNullOrEmpty(settings.SkippedVersion) &&
                                 settings.SkippedVersion.Trim().TrimStart('v', 'V') == cleanTag;

                string downloadUrl = "";
                long fileSize = 0;

                if (root.TryGetProperty("assets", out var assetsProp) && assetsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assetsProp.EnumerateArray())
                    {
                        string assetName = asset.TryGetProperty("name", out var an) ? an.GetString() ?? "" : "";
                        if (assetName.EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase) ||
                            assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = asset.TryGetProperty("browser_download_url", out var dl) ? dl.GetString() ?? "" : "";
                            fileSize = asset.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0;
                            break;
                        }
                    }
                }

                // Fallback direct URL if no asset was found
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    downloadUrl = $"https://github.com/{GitHubRepo}/releases/download/{tagName}/Tempo-Setup.exe";
                }

                // Extract SHA256 if included in release body
                string expectedSha256 = ExtractSha256FromBody(releaseBody);

                return new UpdateInfo
                {
                    IsUpdateAvailable = isNewer && !isSkipped,
                    CurrentVersion = currentVersion.ToString(3),
                    LatestVersion = cleanTag,
                    ReleaseName = string.IsNullOrEmpty(releaseName) ? $"Tempo v{cleanTag}" : releaseName,
                    ReleaseNotes = releaseBody,
                    DownloadUrl = downloadUrl,
                    ExpectedSha256 = expectedSha256,
                    FileSize = fileSize,
                    PublishedAt = publishedAt
                };
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or IOException)
            {
                return null;
            }
        }

        public async Task<string> DownloadInstallerAsync(
            string downloadUrl,
            string expectedSha256,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            // Security Gate 1: Strict HTTPS enforcement
            if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidOperationException("Insecure download URL rejected. Updates require HTTPS.");
            }

            // Security Gate 2: Validate payload extension (.exe only)
            if (!uri.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Invalid update payload type. Only installer .exe packages are permitted.");
            }

            // Security Gate 3: Mandatory cryptographic SHA256 verification (no bypass allowed)
            if (string.IsNullOrWhiteSpace(expectedSha256) || expectedSha256.Trim().Length != 64)
            {
                throw new InvalidDataException("Update rejected: Missing or invalid cryptographic SHA256 checksum in release metadata.");
            }

            string tempDir = Path.Combine(Path.GetTempPath(), "Tempo-Update");
            if (!Directory.Exists(tempDir))
            {
                Directory.CreateDirectory(tempDir);
            }

            // Security Gate 4: Unpredictable random filename to prevent symlink/race-condition hijacking
            string randomFilename = $"Tempo-Setup-{Guid.NewGuid():N}.exe";
            string targetFile = Path.Combine(tempDir, randomFilename);

            try
            {
                using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                long totalBytes = response.Content.Headers.ContentLength ?? -1L;
                const long MaxDownloadBytes = 100 * 1024 * 1024; // 100 MB maximum threshold
                if (totalBytes > MaxDownloadBytes)
                {
                    throw new InvalidDataException($"Update rejected: Content-Length ({totalBytes / (1024 * 1024)} MB) exceeds 100 MB safety threshold.");
                }

                using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var fileStream = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    totalRead += bytesRead;
                    if (totalRead > MaxDownloadBytes)
                    {
                        throw new InvalidDataException("Update rejected: Download stream exceeded 100 MB safety threshold.");
                    }

                    await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);

                    if (totalBytes > 0 && progress != null)
                    {
                        int percentage = (int)((totalRead * 100) / totalBytes);
                        progress.Report(percentage);
                    }
                }

                fileStream.Close();

                // Security Gate 5: Strict SHA256 verification
                if (!VerifySha256(targetFile, expectedSha256))
                {
                    try { if (File.Exists(targetFile)) File.Delete(targetFile); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                    throw new InvalidDataException("SHA256 checksum verification failed. The downloaded file might be corrupted or tampered with.");
                }

                return targetFile;
            }
            catch (Exception)
            {
                try { if (File.Exists(targetFile)) File.Delete(targetFile); } catch (Exception delEx) when (delEx is IOException or UnauthorizedAccessException) { }
                throw;
            }
        }

        public static bool VerifySha256(string filePath, string expectedHash)
        {
            if (!File.Exists(filePath) || string.IsNullOrWhiteSpace(expectedHash)) return false;
            try
            {
                string cleanExpected = expectedHash.Trim();
                if (cleanExpected.Length != 64) return false;

                using var sha256 = SHA256.Create();
                using var stream = File.OpenRead(filePath);
                byte[] hashBytes = sha256.ComputeHash(stream);
                byte[] expectedBytes = Convert.FromHexString(cleanExpected);

                return CryptographicOperations.FixedTimeEquals(hashBytes, expectedBytes);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
            {
                return false;
            }
        }

        public static UpdateInstallStatus LaunchInstaller(string installerPath, bool silent = true)
        {
            if (!File.Exists(installerPath))
            {
                return UpdateInstallStatus.FileNotFound;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = installerPath,
                    Arguments = silent ? "/SILENT /NORESTART /CLOSEAPPLICATIONS" : "",
                    UseShellExecute = true,
                    Verb = "runas" // Request UAC elevation for Program Files installation
                };

                Process.Start(psi);
                return UpdateInstallStatus.Success;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // Error 1223: The operation was canceled by the user (UAC prompt denied)
                return UpdateInstallStatus.UserCancelledUac;
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                return UpdateInstallStatus.Failed;
            }
        }

        private static string ExtractSha256FromBody(string body)
        {
            if (string.IsNullOrEmpty(body)) return "";

            // 1. Direct tag: SHA256: <hash> or SHA-256: <hash>
            var match = Regex.Match(body, @"SHA-?256[:\s=]+([a-fA-F0-9]{64})", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }

            // 2. Setup file line: Tempo-Setup...exe: <hash> or `hash`
            var matchFile = Regex.Match(body, @"Tempo-Setup[^\r\n]*?[:\s`|]+([a-fA-F0-9]{64})", RegexOptions.IgnoreCase);
            if (matchFile.Success)
            {
                return matchFile.Groups[1].Value.Trim();
            }

            return "";
        }
    }
}
