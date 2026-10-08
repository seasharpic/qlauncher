using System;
using System.Collections.Generic;
using System.Windows;

namespace MinecraftLauncher.Services
{
    public interface ILocalizationService
    {
        string CurrentLanguage { get; }
        event Action? LanguageChanged;
        void SetLanguage(string languageCode);
        string GetString(string key, string fallback = "");

        /// <summary>
        /// Возвращает строку с подстановкой одного параметра.
        /// Нужен там, где текст строится в коде: "{0}" заменяется значением.
        /// </summary>
        string Format(string key, object arg0);

        /// <summary>Возвращает строку с подстановкой двух параметров.</summary>
        string Format(string key, object arg0, object arg1);

        /// <summary>Возвращает строку с подстановкой трёх параметров.</summary>
        string Format(string key, object arg0, object arg1, object arg2);

        /// <summary>Возвращает строку с подстановкой четырёх параметров.</summary>
        string Format(string key, object arg0, object arg1, object arg2, object arg3);

        /// <summary>
        /// Словарь в текущем языке. Нужен, чтобы язык можно было переключить
        /// не только в XAML (где работает DynamicResource), но и в обычном
        /// коде — там ресурсы не подставляются автоматически.
        /// </summary>
        IReadOnlyDictionary<string, string> Strings { get; }
    }

    public class LocalizationService : ILocalizationService
    {
        private static readonly IReadOnlyDictionary<string, string> EmptyStrings =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private IReadOnlyDictionary<string, string> _strings = EmptyStrings;

        public static LocalizationService Instance { get; } = new LocalizationService();

        public string CurrentLanguage { get; private set; } = "ru";
        public event Action? LanguageChanged;

        public IReadOnlyDictionary<string, string> Strings => _strings;

        public void SetLanguage(string languageCode)
        {
            string code = string.Equals(languageCode, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "ru";
            CurrentLanguage = code;

            try
            {
                var uri = new Uri($"pack://application:,,,/Resources/Strings.{code}.xaml", UriKind.Absolute);
                var dict = new ResourceDictionary { Source = uri };

                // Копия текущих строк нужна коду: в XAML подстановкой занимается
                // DynamicResource, а здесь мы обращаемся к словарю напрямую.
                _strings = BuildStringMap(dict);

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

        /// <summary>
        /// Извлекает sys:String-ключи из словаря в обычный Dictionary.
        /// Копия нужна потому, что сам ResourceDictionary живёт только в
        /// Application.Current.Resources, а к нему нельзя обращаться с
        /// фонового потока.
        /// </summary>
        private static IReadOnlyDictionary<string, string> BuildStringMap(ResourceDictionary dict)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);

            try
            {
                foreach (object key in dict.Keys)
                {
                    if (key is string name && dict[name] is string value)
                    {
                        map[name] = value;
                    }
                }
            }
            catch
            {
                return EmptyStrings;
            }

            return map.Count > 0 ? map : EmptyStrings;
        }

        public string GetString(string key, string fallback = "")
        {
            // Сначала копия: она одинаково доступна с любого потока.
            if (_strings.TryGetValue(key, out string? cached))
            {
                return cached;
            }

            if (Application.Current?.Resources.Contains(key) == true)
            {
                return Application.Current.Resources[key]?.ToString() ?? fallback;
            }

            return fallback;
        }

        public string Format(string key, object arg0)
        {
            return string.Format(GetString(key), arg0);
        }

        public string Format(string key, object arg0, object arg1)
        {
            return string.Format(GetString(key), arg0, arg1);
        }

        public string Format(string key, object arg0, object arg1, object arg2)
        {
            return string.Format(GetString(key), arg0, arg1, arg2);
        }

        public string Format(string key, object arg0, object arg1, object arg2, object arg3)
        {
            return string.Format(GetString(key), arg0, arg1, arg2, arg3);
        }
    }
}
