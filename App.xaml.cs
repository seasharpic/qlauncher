using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
                // args.Handled = true оставляем: лаунчер продолжает работать после
                // ошибки в UI. Но теперь попутно пишем в лог потокобезопасно и
                // показываем пользователю не только Message, но и тип: раньше
                // "Object reference not set" без контекста ни о чём не говорил.
                CrashLogWriter.Write("DispatcherUnhandledException", "Unhandled UI exception", args.Exception);

                try
                {
                    MessageBox.Show(
                        $"An error occurred while running the application:\n\n" +
                        $"{args.Exception.GetType().Name}: {args.Exception.Message}\n\n" +
                        $"Details were written to:\n{CrashLogWriter.LogFile}",
                        "QLauncher",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                catch { }

                args.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                var exception = args.ExceptionObject as Exception;
                CrashLogWriter.Write(
                    "AppDomainUnhandledException",
                    "Fatal unhandled exception on background thread",
                    exception);
            };

            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                // Раньше исключения из fire-and-forget задач (Task.Run без await)
                // терялись без следа — например, ошибки загрузки версий.
                CrashLogWriter.Write("UnobservedTaskException", "Background task faulted", args.Exception);
                args.SetObserved();
            };

            LauncherPathHelper.CleanupOldBackupsAndTemp();

            // Определяем, какие источники загрузки доступны, и заранее
            // переставляем зеркала. Проверка идёт в фоне и не блокирует
            // показ окна: пока результата нет, загрузка идёт как раньше —
            // официальный источник первым.
            MirrorService.Instance.Preference = MirrorService.ParsePreference(
                SettingsService.Instance.Settings.MirrorPreference);
            _ = Task.Run(() => MirrorService.Instance.ProbeAsync());

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

                // Акцент применяется после темы: SetTheme переносит текущие
                // значения акцента на новую палитру кистей.
                ThemeService.Instance.RestoreAccentFromSettings();

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

