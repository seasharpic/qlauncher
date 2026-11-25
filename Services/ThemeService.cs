using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace MinecraftLauncher.Services
{
    public interface IThemeService
    {
        bool IsDarkTheme { get; set; }
        event Action? ThemeChanged;
        event Action? AccentChanged;
        void ToggleTheme();
        void SetTheme(bool isDark);
        void ApplyAccentColor(string mainHex, string hoverHex);
        void ApplyAccentPreset(string presetName);
        string CurrentAccentPreset { get; }
    }

    public class ThemeService : IThemeService
    {
        public static ThemeService Instance { get; } = new ThemeService();

        public bool IsDarkTheme { get; set; } = true;
        public event Action? ThemeChanged;
        public event Action? AccentChanged;

        private string? _currentAccentMain;
        private string _currentAccentPreset = "sapphire";
        private string? _currentAccentHover;

        public void SetTheme(bool isDark)
        {
            IsDarkTheme = isDark;
            string themeName = IsDarkTheme ? "DarkTheme.axaml" : "LightTheme.axaml";
            var uri = new Uri($"avares://QLauncher/Themes/{themeName}");

            void Apply()
            {
                try
                {
                    var app = Application.Current;
                    if (app != null)
                    {
                        app.RequestedThemeVariant = IsDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
                        if (AvaloniaXamlLoader.Load(uri) is ResourceDictionary dict)
                        {
                            foreach (var kv in dict)
                            {
                                app.Resources[kv.Key] = kv.Value;
                            }
                        }

                        if (!string.IsNullOrEmpty(_currentAccentMain) && !string.IsNullOrEmpty(_currentAccentHover))
                        {
                            ApplyAccentColor(_currentAccentMain, _currentAccentHover);
                        }
                    }
                }
                catch { }
            }

            if (Dispatcher.UIThread.CheckAccess())
                Apply();
            else
                Dispatcher.UIThread.Post(Apply);

            try
            {
                var settings = SettingsService.Instance.Settings;
                if (settings.IsDarkTheme != isDark)
                {
                    settings.IsDarkTheme = isDark;
                    SettingsService.Instance.Save();
                }
            }
            catch { }

            ThemeChanged?.Invoke();
        }

        public void ToggleTheme()
        {
            SetTheme(!IsDarkTheme);
        }

        public void ApplyAccentColor(string mainHex, string hoverHex)
        {
            try
            {
                _currentAccentMain = mainHex;
                _currentAccentHover = hoverHex;

                var mainColor = Color.Parse(mainHex);
                var hoverColor = Color.Parse(hoverHex);

                void Apply()
                {
                    if (Application.Current?.Resources == null) return;
                    Application.Current.Resources["AccentBrush"] = new SolidColorBrush(mainColor);
                    Application.Current.Resources["AccentHoverBrush"] = new SolidColorBrush(hoverColor);
                    Application.Current.Resources["AccentClickBrush"] = new SolidColorBrush(mainColor);
                }

                if (Dispatcher.UIThread.CheckAccess())
                    Apply();
                else
                    Dispatcher.UIThread.Post(Apply);
            }
            catch { }
        }

        public void ApplyAccentPreset(string presetName)
        {
            switch (presetName.ToLowerInvariant())
            {
                case "emerald":
                    ApplyAccentColor("#10B981", "#34D399");
                    break;
                case "purple":
                case "amethyst":
                    ApplyAccentColor("#8B5CF6", "#A78BFA");
                    break;
                case "rose":
                case "pink":
                    ApplyAccentColor("#F43F5E", "#FB7185");
                    break;
                case "amber":
                case "gold":
                    ApplyAccentColor("#F59E0B", "#FBBF24");
                    break;
                case "nether":
                    ApplyAccentColor("#D64541", "#FF7A45");
                    break;
                case "end":
                    ApplyAccentColor("#5A2EC9", "#9E7CFF");
                    break;
                case "sift":
                    ApplyAccentColor("#12A5A5", "#5CE1E6");
                    break;
                case "sapphire":
                case "blue":
                default:
                    ApplyAccentColor("#3B85E6", "#6AC4F7");
                    break;
            }

            RememberAccentPreset(presetName);
            AccentChanged?.Invoke();
        }

        private void RememberAccentPreset(string presetName)
        {
            string canonical = presetName.ToLowerInvariant() switch
            {
                "amethyst" => "purple",
                "pink" => "rose",
                "gold" => "amber",
                "blue" => "sapphire",
                "emerald" or "purple" or "rose" or "amber" or "sapphire"
                    or "nether" or "end" or "sift" => presetName.ToLowerInvariant(),
                _ => "sapphire",
            };

            _currentAccentPreset = canonical;

            try
            {
                var settings = SettingsService.Instance.Settings;
                if (!string.Equals(settings.AccentPreset, canonical, StringComparison.OrdinalIgnoreCase))
                {
                    settings.AccentPreset = canonical;
                    SettingsService.Instance.Save();
                }
            }
            catch { }
        }

        public void RestoreAccentFromSettings()
        {
            string savedPreset = SettingsService.Instance.Settings.AccentPreset ?? "sapphire";
            RememberAccentPreset(savedPreset);

            try
            {
                ApplyAccentColor(ResolvePresetMain(savedPreset),
                                 ResolvePresetHover(savedPreset));
            }
            catch { }
        }

        public string CurrentAccentPreset => _currentAccentPreset;

        private static string ResolvePresetMain(string? presetName) =>
            presetName?.ToLowerInvariant() switch
            {
                "emerald" => "#10B981",
                "purple" => "#8B5CF6",
                "rose" => "#F43F5E",
                "amber" => "#F59E0B",
                "nether" => "#D64541",
                "end" => "#5A2EC9",
                "sift" => "#12A5A5",
                _ => "#3B85E6",
            };

        private static string ResolvePresetHover(string? presetName) =>
            presetName?.ToLowerInvariant() switch
            {
                "emerald" => "#34D399",
                "purple" => "#A78BFA",
                "rose" => "#FB7185",
                "amber" => "#FBBF24",
                "nether" => "#FF7A45",
                "end" => "#9E7CFF",
                "sift" => "#5CE1E6",
                _ => "#6AC4F7",
            };
    }
}
