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
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                var uri = new Uri($"avares://QLauncher/Resources/Strings.{code}.axaml");
                if (AvaloniaXamlLoader.Load(uri) is ResourceDictionary dict)
                {
                    foreach (var kv in dict)
                    {
                        if (kv.Key is string name && kv.Value is string value)
                        {
                            map[name] = value;
                        }
                    }
                }
            }
            catch { }

            ApplyOsOverrides(map, code);
            return map;
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

                        var overrides = new Dictionary<string, string>(StringComparer.Ordinal);
                        ApplyOsOverrides(overrides, code);
                        foreach (var kv in overrides)
                        {
                            Application.Current.Resources[kv.Key] = kv.Value;
                        }
                    }
                }
                catch { }
            });
        }

        private static void ApplyOsOverrides(IDictionary<string, string> map, string code)
        {
            if (OperatingSystem.IsWindows()) return;

            bool isRu = string.Equals(code, "ru", StringComparison.OrdinalIgnoreCase);

            if (OperatingSystem.IsLinux())
            {
                map["Str_Profile_Menu_ShowInExplorer"] = isRu ? "Показать в файловом менеджере" : "Show in File Manager";
                map["Str_Gpu_Default"] = isRu ? "По умолчанию Linux (Автовыбор)" : "Linux default (auto-select)";
                map["Str_Gpu_SummaryDefault"] = isRu ? "Автовыбор системы ({0})" : "System auto-select ({0})";
                map["Str_Gpu_ModeDefault"] = isRu ? "Режим: По умолчанию Linux" : "Mode: Linux default";
                map["Str_Gpu_DetailHighPerf"] = isRu
                    ? "Система и видеодрайвер (DRI_PRIME / PRIME Offload) направят игру на дискретный GPU."
                    : "Linux and the GPU driver (DRI_PRIME / PRIME Offload) will run the game on the dedicated GPU.";
                map["Str_Dialog_JavaFilter"] = "Java Binary (java)|java|All files (*)|*";
                map["Str_Java_JavawMissing"] = isRu
                    ? "Исполняемый файл java не найден в распакованном архиве."
                    : "java executable was not found in the unpacked archive.";
            }
            else if (OperatingSystem.IsMacOS())
            {
                map["Str_Profile_Menu_ShowInExplorer"] = isRu ? "Показать в Finder" : "Show in Finder";
                map["Str_Gpu_Default"] = isRu ? "По умолчанию macOS (Автовыбор)" : "macOS default (auto-select)";
                map["Str_Gpu_SummaryDefault"] = isRu ? "Автовыбор системы ({0})" : "System auto-select ({0})";
                map["Str_Gpu_ModeDefault"] = isRu ? "Режим: По умолчанию macOS" : "Mode: macOS default";
                map["Str_Gpu_DetailHighPerf"] = isRu
                    ? "macOS и видеодрайвер направят процесс игры на дискретный графический чип."
                    : "macOS and the graphics driver will run the game on the dedicated GPU.";
                map["Str_Dialog_JavaFilter"] = "Java Binary (java)|java|All files (*)|*";
                map["Str_Java_JavawMissing"] = isRu
                    ? "Исполняемый файл java не найден в распакованном архиве."
                    : "java executable was not found in the unpacked archive.";
            }
            else if (OperatingSystem.IsAndroid())
            {
                map["Str_Profile_Menu_ShowInExplorer"] = isRu ? "Показать в файлах" : "Show in Files";
                map["Str_Gpu_Default"] = isRu ? "По умолчанию Android (Автовыбор)" : "Android default (auto-select)";
                map["Str_Gpu_SummaryDefault"] = isRu ? "Автовыбор системы ({0})" : "System auto-select ({0})";
                map["Str_Gpu_ModeDefault"] = isRu ? "Режим: По умолчанию Android" : "Mode: Android default";
                map["Str_Gpu_DetailHighPerf"] = isRu
                    ? "Android использует системный графический драйвер устройства."
                    : "Android will use the device's system graphics driver.";
                map["Str_Dialog_JavaFilter"] = "All files (*)|*";
                map["Str_Java_JavawMissing"] = isRu
                    ? "Исполняемый файл java не найден в распакованном архиве."
                    : "java executable was not found in the unpacked archive.";
            }
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
