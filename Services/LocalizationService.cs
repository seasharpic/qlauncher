using System;
using System.Windows;

namespace MinecraftLauncher.Services
{
    public interface ILocalizationService
    {
        string CurrentLanguage { get; }
        event Action? LanguageChanged;
        void SetLanguage(string languageCode);
        string GetString(string key, string fallback = "");
    }

    public class LocalizationService : ILocalizationService
    {
        public static LocalizationService Instance { get; } = new LocalizationService();

        public string CurrentLanguage { get; private set; } = "ru";
        public event Action? LanguageChanged;

        public void SetLanguage(string languageCode)
        {
            string code = string.Equals(languageCode, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "ru";
            CurrentLanguage = code;

            try
            {
                var uri = new Uri($"pack://application:,,,/Resources/Strings.{code}.xaml", UriKind.Absolute);
                var dict = new ResourceDictionary { Source = uri };

                if (Application.Current != null && Application.Current.Resources != null)
                {
                    var merged = Application.Current.Resources.MergedDictionaries;
                    ResourceDictionary? existing = null;
                    foreach (var md in merged)
                    {
                        if (md.Source != null && (md.Source.OriginalString.Contains("Strings.ru.xaml") ||
                                                  md.Source.OriginalString.Contains("Strings.en.xaml")))
                        {
                            existing = md;
                            break;
                        }
                    }

                    if (existing != null)
                    {
                        merged.Remove(existing);
                    }
                    merged.Add(dict);

                    foreach (var key in dict.Keys)
                    {
                        Application.Current.Resources[key] = dict[key];
                    }
                }

                var settings = SettingsService.Instance.Settings;
                if (settings.Language != code)
                {
                    settings.Language = code;
                    SettingsService.Instance.Save();
                }
            }
            catch { }

            LanguageChanged?.Invoke();
        }

        public string GetString(string key, string fallback = "")
        {
            if (Application.Current?.Resources.Contains(key) == true)
            {
                return Application.Current.Resources[key]?.ToString() ?? fallback;
            }
            return fallback;
        }
    }
}
