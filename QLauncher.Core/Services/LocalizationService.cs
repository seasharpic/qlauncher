using System;
using System.Collections.Generic;

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
        /// не только в XAML, но и в обычном коде.
        /// </summary>
        IReadOnlyDictionary<string, string> Strings { get; }
    }

    /// <summary>
    /// Платформо-независимый движок локализации. Живёт в ядре, поэтому
    /// о WPF (Application.Current, pack:// URI) ничего не знает: словари
    /// строк в него поставляет хост-приложение через статический
    /// DictionarySource, а на смену языка уведомляет через LanguageMerged —
    /// UI сам обновляет свои ресурсные словари. Логика выбора строк,
    /// сохранения языка в настройках и всех Format-перегрузок — без изменений.
    /// </summary>
    public class LocalizationService : ILocalizationService
    {
        private static readonly IReadOnlyDictionary<string, string> EmptyStrings =
            new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Хост отдаёт строки для кода языка. Возвращать можно null —
        /// ядро продолжит работать с предыдущим (или пустым) словарём.
        /// </summary>
        public static Func<string, IReadOnlyDictionary<string, string>?>? DictionarySource { get; set; }

        /// <summary>
        /// Уведомление хосту после смены языка: UI перезаливает свой
        /// ResourceDictionary, чтобы обновился DynamicResource в XAML.
        /// </summary>
        public static Action<string>? LanguageMerged { get; set; }

        private IReadOnlyDictionary<string, string> _strings = EmptyStrings;

        public static LocalizationService Instance { get; } = new LocalizationService();

        public string CurrentLanguage { get; private set; } = "ru";
        public event Action? LanguageChanged;

        public IReadOnlyDictionary<string, string> Strings => _strings;

        public void SetLanguage(string languageCode)
        {
            string code = string.Equals(languageCode, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "ru";
            CurrentLanguage = code;

            // Копия текущих строк нужна коду: в XAML подстановкой занимается
            // DynamicResource, а здесь мы обращаемся к словарю напрямую.
            var map = DictionarySource?.Invoke(code);
            _strings = map ?? EmptyStrings;

            try
            {
                var settings = SettingsService.Instance.Settings;
                if (settings.Language != code)
                {
                    settings.Language = code;
                    SettingsService.Instance.Save();
                }
            }
            catch { }

            LanguageMerged?.Invoke(code);
            LanguageChanged?.Invoke();
        }

        public string GetString(string key, string fallback = "")
        {
            // Копия строк одинаково доступна с любого потока.
            if (_strings.TryGetValue(key, out string? cached))
            {
                return cached;
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
