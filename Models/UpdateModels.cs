using System;
using System.IO;
using System.Text.Json;

namespace Tempo.Models
{
    public class UpdateInfo
    {
        public bool IsUpdateAvailable { get; set; }
        public string CurrentVersion { get; set; } = "2.2.0";
        public string LatestVersion { get; set; } = "";
        public string ReleaseName { get; set; } = "";
        public string ReleaseNotes { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public string ExpectedSha256 { get; set; } = "";
        public long FileSize { get; set; }
        public DateTime? PublishedAt { get; set; }
    }

    public class UpdateSettings
    {
        public DateTime? LastCheckTimeUtc { get; set; }
        public string? SkippedVersion { get; set; }

        private static readonly string SettingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tempo");
        private static readonly string SettingsFilePath = Path.Combine(SettingsDir, "update_settings.json");

        public static UpdateSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    return JsonSerializer.Deserialize<UpdateSettings>(json) ?? new UpdateSettings();
                }
            }
            catch
            {
                // Fallback to defaults on corrupt settings
            }
            return new UpdateSettings();
        }

        public void Save()
        {
            try
            {
                if (!Directory.Exists(SettingsDir))
                {
                    Directory.CreateDirectory(SettingsDir);
                }
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFilePath, json);
            }
            catch
            {
                // Non-critical write failure
            }
        }
    }
}
