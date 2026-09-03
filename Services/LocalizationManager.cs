using System;
using System.Linq;
using System.Windows;

namespace Tempo.Services
{
    public static class LocalizationManager
    {
        public static string CurrentLanguage { get; private set; } = "en"; // Default is English!
        public static bool IsRtl => CurrentLanguage == "ar";

        public static event Action<string>? LanguageChanged;

        public static void Initialize(string? savedLanguage)
        {
            // Default to English if null or unspecified
            string lang = string.Equals(savedLanguage, "ar", StringComparison.OrdinalIgnoreCase) ? "ar" : "en";
            SetLanguage(lang, notify: false);
        }

        public static void SetLanguage(string lang, bool notify = true)
        {
            CurrentLanguage = string.Equals(lang, "ar", StringComparison.OrdinalIgnoreCase) ? "ar" : "en";

            try
            {
                var app = Application.Current;
                if (app != null)
                {
                    string dictUriString = $"pack://application:,,,/Resources/Strings.{CurrentLanguage}.xaml";
                    var newDict = new ResourceDictionary { Source = new Uri(dictUriString, UriKind.RelativeOrAbsolute) };

                    // Find existing Strings dictionary if any
                    var existing = app.Resources.MergedDictionaries
                        .FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("Strings."));

                    if (existing != null)
                    {
                        int index = app.Resources.MergedDictionaries.IndexOf(existing);
                        app.Resources.MergedDictionaries[index] = newDict;
                    }
                    else
                    {
                        app.Resources.MergedDictionaries.Add(newDict);
                    }
                }
            }
            catch
            {
                // Fallback gracefully
            }

            if (notify)
            {
                LanguageChanged?.Invoke(CurrentLanguage);
            }
        }

        public static string GetString(string key, string fallback = "")
        {
            try
            {
                var res = Application.Current?.TryFindResource(key);
                if (res is string s && !string.IsNullOrEmpty(s))
                {
                    return s;
                }
            }
            catch
            {
            }
            return fallback;
        }

        // LTR/RTL Safe Number & Unit Formatting Helpers (Always Left-to-Right Mark isolated)
        public static string FormatMb(double mb) => $"\u200E{mb:F1} MB\u200E";
        public static string FormatGb(double gb) => $"\u200E{gb:F1} GB\u200E";
        public static string FormatPercent(double val) => $"\u200E{val:F0}%\u200E";

        public static string FormatSpeed(double kbSec)
        {
            if (kbSec >= 1024)
            {
                return $"\u200E{kbSec / 1024.0:F1} MB/s\u200E";
            }
            return $"\u200E{kbSec:F1} KB/s\u200E";
        }

        public static string FormatRamSummary(double usedGb, double totalGb)
        {
            if (CurrentLanguage == "ar")
            {
                return $"\u200E{usedGb:F1} GB\u200E من أصل \u200E{totalGb:F1} GB\u200E";
            }
            return $"\u200E{usedGb:F1} GB\u200E of \u200E{totalGb:F1} GB\u200E";
        }

        public static string FormatStorageSummary(double freeGb, double totalGb, double usedPercent)
        {
            if (CurrentLanguage == "ar")
            {
                return $"\u200E{freeGb:F1} GB\u200E متاح من أصل \u200E{totalGb:F1} GB\u200E (\u200E{usedPercent:F0}%\u200E)";
            }
            return $"\u200E{freeGb:F1} GB\u200E free of \u200E{totalGb:F1} GB\u200E (\u200E{usedPercent:F0}%\u200E)";
        }
    }
}
