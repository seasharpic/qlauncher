using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MinecraftLauncher.Helpers;
using MinecraftLauncher.Services;
using MinecraftLauncher.Views;

namespace MinecraftLauncher
{
    public partial class AndroidApp : Avalonia.Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            SecretProtection.Protector = Helpers.SecretProtector.Instance;
            LocalizationUiBridge.Attach();
            LauncherPathHelper.CleanupOldBackupsAndTemp();

            MirrorService.Instance.Preference = MirrorService.ParsePreference(
                SettingsService.Instance.Settings.MirrorPreference);
            _ = Task.Run(() => MirrorService.Instance.ProbeAsync());

            try
            {
                var settings = SettingsService.Instance.Load();
                if (!string.IsNullOrEmpty(settings.Language))
                    LocalizationService.Instance.SetLanguage(settings.Language);
                if (!settings.IsDarkTheme)
                    ThemeService.Instance.SetTheme(false);
                ThemeService.Instance.RestoreAccentFromSettings();
            }
            catch { }

            if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
            {
                singleView.MainView = new AndroidMainView();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
