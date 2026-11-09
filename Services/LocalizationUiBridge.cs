using System;
using System.Collections.Generic;
using System.Windows;

namespace MinecraftLauncher.Services
{
    /// <summary>
    /// Мост «ядро локализации → WPF». Ядро (QLauncher.Core) не знает про
    /// Application.Current и pack:// URI: здесь живёт единственное место,
    /// которое загружает Strings.ru/en.xaml и перезаливает их в ресурсы
    /// приложения. Регистрируется вызовом Attach() в App.OnStartup — до
    /// первого SetLanguage. GetString-логика ядра осталась прежней, поэтому
    /// ни один вызов в UI не меняется.
    /// </summary>
    public static class LocalizationUiBridge
    {
        private static readonly ILocalizationService _loc = LocalizationService.Instance;

        /// <summary>Подключает WPF-источники строк к движку ядра.</summary>
        public static void Attach()
        {
            LocalizationService.DictionarySource = code => LoadIntoMap(code);
            LocalizationService.LanguageMerged = code => MergeIntoApp(code);
        }

        /// <summary>
        /// Загружает словарь строк для кода языка. Вызывается на потоке,
        /// вызвавшем SetLanguage: движку нужна только копия строк, так что
        /// можно из любого потока.
        /// </summary>
        private static IReadOnlyDictionary<string, string> LoadIntoMap(string code)
        {
            try
            {
                var uri = new Uri($"pack://application:,,,/Resources/Strings.{code}.xaml", UriKind.Absolute);
                var dict = new System.Windows.ResourceDictionary { Source = uri };

                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (object key in dict.Keys)
                {
                    if (key is string name && dict[name] is string value)
                    {
                        map[name] = value;
                    }
                }

                return map;
            }
            catch
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }
        }

        /// <summary>
        /// Перезаливает System.Xaml-словарь в Application.Resources: удаляет
        /// старый Strings.*.xaml, добавляет новый и прописывает ключи напрямую
        /// (по ним работает DynamicResource в XAML). Работает только на UI-
        /// потоке, поэтому из другого потока пережимается через Dispatcher.
        /// </summary>
        private static void MergeIntoApp(string code)
        {
            RunOnUiThread(() =>
            {
                try
                {
                    if (Application.Current?.Resources == null) return;

                    var uri = new Uri($"pack://application:,,,/Resources/Strings.{code}.xaml", UriKind.Absolute);
                    var dict = new ResourceDictionary { Source = uri };

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
                catch { }
            });
        }

        private static void RunOnUiThread(Action action)
        {
            var app = Application.Current;
            if (app == null)
            {
                return;
            }

            if (app.CheckAccess())
            {
                action();
            }
            else
            {
                app.Dispatcher.Invoke(action);
            }
        }
    }
}
