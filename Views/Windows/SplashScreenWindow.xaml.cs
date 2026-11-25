using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using MinecraftLauncher.Services;

namespace MinecraftLauncher.Views.Windows
{
    public partial class SplashScreenWindow : Window
    {
        private static readonly ILocalizationService L = LocalizationService.Instance;

        public SplashScreenWindow()
        {
            InitializeComponent();
            Opened += Window_Opened;
        }

        private async void Window_Opened(object? sender, EventArgs e)
        {
            await SimulateLoadingAsync();

            var mainWindow = new MainWindow();
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = mainWindow;
                mainWindow.Closed += (_, _) => desktop.Shutdown();
            }
            mainWindow.Show();

            Close();
        }

        private async Task SimulateLoadingAsync()
        {
            StatusText.Text = L.GetString("Str_Splash_Initializing");
            ProgressBarFill.Width = 80;
            await Task.Delay(400);

            StatusText.Text = L.GetString("Str_Splash_LoadingConfig");
            ProgressBarFill.Width = 180;
            await Task.Delay(450);

            StatusText.Text = L.GetString("Str_Splash_CheckingComponents");
            ProgressBarFill.Width = 260;
            await Task.Delay(400);

            StatusText.Text = L.GetString("Str_Splash_Ready");
            ProgressBarFill.Width = 300;
            await Task.Delay(250);
        }
    }
}
