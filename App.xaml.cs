using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MinecraftLauncher.Helpers;
using MinecraftLauncher.Services;
using MinecraftLauncher.Views.Windows;

namespace MinecraftLauncher
{
    public partial class App : Application
    {
        public static string? JustUpdatedVersion { get; set; }

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            SecretProtection.Protector = Helpers.SecretProtector.Instance;
            LocalizationUiBridge.Attach();

            PlatformCapabilities.PrepareLaunchEnvironment = (javaPath, preference, psi) =>
            {
                GpuService.Instance.ApplyGpuPreference(javaPath, preference);
                GpuService.Instance.ConfigureProcessEnvironment(psi, preference);
            };
            PlatformCapabilities.EnumerateGpus = () => GpuService.Instance.GetAvailableGpus();

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
                CrashLogWriter.Write("UnobservedTaskException", "Background task faulted", args.Exception);
                args.SetObserved();
            };

            LauncherPathHelper.CleanupOldBackupsAndTemp();

            MirrorService.Instance.Preference = MirrorService.ParsePreference(
                SettingsService.Instance.Settings.MirrorPreference);
            _ = Task.Run(() => MirrorService.Instance.ProbeAsync());

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var args = desktop.Args ?? Array.Empty<string>();

                bool noSplashArg = args.Any(a => string.Equals(a, "--no-splash", StringComparison.OrdinalIgnoreCase));

                for (int i = 0; i < args.Length; i++)
                {
                    if (string.Equals(args[i], "--updated", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    {
                        JustUpdatedVersion = args[i + 1];
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

                    ThemeService.Instance.RestoreAccentFromSettings();

                    if (noSplashArg || !settings.ShowSplashOnStartup)
                    {
                        var mainWindow = new MainWindow();
                        desktop.MainWindow = mainWindow;
                        mainWindow.Closed += (_, _) => desktop.Shutdown();
                        mainWindow.Show();
                        base.OnFrameworkInitializationCompleted();
                        return;
                    }
                }
                catch { }

                var splash = new SplashScreenWindow();
                desktop.MainWindow = splash;
                splash.Show();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
