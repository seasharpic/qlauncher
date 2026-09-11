using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;

namespace MinecraftLauncher.Views.Windows
{
    public partial class SplashScreenWindow : Window
    {
        public SplashScreenWindow()
        {
            InitializeComponent();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await SimulateLoadingAsync();

            var mainWindow = new MainWindow();
            Application.Current.MainWindow = mainWindow;
            mainWindow.Closed += (_, _) => Application.Current.Shutdown();
            mainWindow.Show();

            Close();
        }

        private async Task SimulateLoadingAsync()
        {
            StatusText.Text = "Инициализация модулей лаунчера...";
            AnimateProgressBar(80);
            await Task.Delay(400);

            StatusText.Text = "Загрузка конфигурации и профилей...";
            AnimateProgressBar(180);
            await Task.Delay(450);

            StatusText.Text = "Проверка обновлений компонентов...";
            AnimateProgressBar(260);
            await Task.Delay(400);

            StatusText.Text = "Готово!";
            AnimateProgressBar(300);
            await Task.Delay(250);
        }

        private void AnimateProgressBar(double toWidth)
        {
            var anim = new DoubleAnimation(toWidth, TimeSpan.FromMilliseconds(350))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            ProgressBarFill.BeginAnimation(FrameworkElement.WidthProperty, anim);
        }
    }
}
