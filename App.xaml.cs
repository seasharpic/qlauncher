using System;
using System.IO;
using System.Linq;
using System.Windows;
using MinecraftLauncher.Helpers;
using MinecraftLauncher.Services;
using MinecraftLauncher.Views.Windows;

namespace MinecraftLauncher
{
    public partial class App : Application
    {
        public static string? JustUpdatedVersion { get; set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += (s, args) =>
            {
                try
                {
                    string crashLog = Path.Combine(LauncherPathHelper.GetDefaultDataDirectory(), "launcher-crash.log");
                    File.AppendAllText(crashLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] DispatcherUnhandledException:\n{args.Exception}\n\n");
                    MessageBox.Show($"Произошла ошибка в работе приложения:\n{args.Exception.Message}\n\nЖурнал ошибки записан в:\n{crashLog}", "QLauncher", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch { }
                args.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                try
                {
                    string crashLog = Path.Combine(LauncherPathHelper.GetDefaultDataDirectory(), "launcher-crash.log");
                    File.AppendAllText(crashLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] AppDomain UnhandledException:\n{args.ExceptionObject}\n\n");
                }
                catch { }
            };

            LauncherPathHelper.CleanupOldBackupsAndTemp();

            bool noSplashArg = e.Args.Any(a => string.Equals(a, "--no-splash", StringComparison.OrdinalIgnoreCase));

            for (int i = 0; i < e.Args.Length; i++)
            {
                if (string.Equals(e.Args[i], "--updated", StringComparison.OrdinalIgnoreCase) && i + 1 < e.Args.Length)
                {
                    JustUpdatedVersion = e.Args[i + 1];
                    break;
                }
            }

            try
            {
                var settings = SettingsService.Instance.Load();
                if (!string.IsNullOrEmpty(settings.Language))
                {
                    LocalizationService.Instance.SetLanguage(settings.Language);
                }

                if (!settings.IsDarkTheme)
                {
                    ThemeService.Instance.SetTheme(false);
                }

                if (noSplashArg || !settings.ShowSplashOnStartup)
                {
                    var mainWindow = new MainWindow();
                    MainWindow = mainWindow;
                    mainWindow.Closed += (_, _) => Shutdown();
                    mainWindow.Show();
                    return;
                }
            }
            catch { }

            var splash = new SplashScreenWindow();
            splash.Show();
        }
    }
}

