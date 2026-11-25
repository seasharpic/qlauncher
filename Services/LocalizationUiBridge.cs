using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace MinecraftLauncher.Services
{
    public static class LocalizationUiBridge
    {
        public static void Attach()
        {
            LocalizationService.DictionarySource = code => LoadIntoMap(code);
            LocalizationService.LanguageMerged = code => MergeIntoApp(code);
        }

        private static IReadOnlyDictionary<string, string> LoadIntoMap(string code)
        {
            try
            {
                var uri = new Uri($"avares://QLauncher/Resources/Strings.{code}.axaml");
                if (AvaloniaXamlLoader.Load(uri) is ResourceDictionary dict)
                {
                    var map = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var kv in dict)
                    {
                        if (kv.Key is string name && kv.Value is string value)
                        {
                            map[name] = value;
                        }
                    }
                    return map;
                }
            }
            catch { }

            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        private static void MergeIntoApp(string code)
        {
            RunOnUiThread(() =>
            {
                try
                {
                    if (Application.Current?.Resources == null) return;

                    var uri = new Uri($"avares://QLauncher/Resources/Strings.{code}.axaml");
                    if (AvaloniaXamlLoader.Load(uri) is ResourceDictionary dict)
                    {
                        foreach (var kv in dict)
                        {
                            Application.Current.Resources[kv.Key] = kv.Value;
                        }
                    }
                }
                catch { }
            });
        }

        private static void RunOnUiThread(Action action)
        {
            if (Dispatcher.UIThread.CheckAccess())
                action();
            else
                Dispatcher.UIThread.Post(action);
        }
    }
}
