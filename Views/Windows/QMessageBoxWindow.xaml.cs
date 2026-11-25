using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using MinecraftLauncher.Helpers;
using MinecraftLauncher.Services;

namespace MinecraftLauncher.Views.Windows
{
    public enum QMessageBoxButton { OK, OKCancel, YesNo, YesNoCancel }
    public enum QMessageBoxImage { None, Information, Warning, Error, Question }
    public enum QMessageBoxResult { None, OK, Cancel, Yes, No }

    public partial class QMessageBoxWindow : Window
    {
        private static readonly ILocalizationService L = LocalizationService.Instance;

        public QMessageBoxResult Result { get; private set; } = QMessageBoxResult.None;

        public QMessageBoxWindow()
        {
            InitializeComponent();
        }

        public QMessageBoxWindow(string message, string? title, QMessageBoxButton buttons, QMessageBoxImage image) : this()
        {
            MessageText.Text = message;
            TitleText.Text = string.IsNullOrEmpty(title)
                ? L.GetString("Str_MessageBox_WindowTitle")
                : title;

            if (image == QMessageBoxImage.Error)
                TopAccent.Background = new SolidColorBrush(Color.Parse("#E81123"));
            else if (image == QMessageBoxImage.Warning)
                TopAccent.Background = new SolidColorBrush(Color.Parse("#F5A623"));
            else
                TopAccent.Background = new SolidColorBrush(Color.Parse("#3B85E6"));

            if (buttons == QMessageBoxButton.YesNo)
            {
                AddButton(L.GetString("Str_Yes"), QMessageBoxResult.Yes, "#3B85E6", true);
                AddButton(L.GetString("Str_No"), QMessageBoxResult.No, "#3A3D4D", false);
            }
            else if (buttons == QMessageBoxButton.OKCancel)
            {
                AddButton(L.GetString("Str_Ok"), QMessageBoxResult.OK, "#3B85E6", true);
                AddButton(L.GetString("Str_Cancel"), QMessageBoxResult.Cancel, "#3A3D4D", false);
            }
            else
            {
                AddButton(L.GetString("Str_Ok"), QMessageBoxResult.OK, "#3B85E6", true);
            }
        }

        private void AddButton(string text, QMessageBoxResult result, string colorHex, bool isPrimary)
        {
            var btn = new Button
            {
                Content = text,
                MinWidth = 90,
                Height = 35,
                Foreground = Brushes.White,
                Cursor = new Cursor(StandardCursorType.Hand),
                Background = new SolidColorBrush(Color.Parse(colorHex)),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(isPrimary ? 6 : 4),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };

            btn.Click += (_, _) =>
            {
                Result = result;
                Close(result);
            };

            ButtonsPanel.Children.Add(btn);
        }

        public static async Task<QMessageBoxResult> ShowAsync(
            string message,
            string? title = null,
            QMessageBoxButton buttons = QMessageBoxButton.OK,
            QMessageBoxImage image = QMessageBoxImage.Information)
        {
            var msgBox = new QMessageBoxWindow(message, title, buttons, image);
            if (UiHelper.MainWindow is { IsVisible: true } owner)
            {
                return await msgBox.ShowDialog<QMessageBoxResult>(owner);
            }

            var tcs = new TaskCompletionSource<QMessageBoxResult>();
            msgBox.Closed += (_, _) => tcs.TrySetResult(msgBox.Result);
            msgBox.Show();
            return await tcs.Task;
        }
    }
}
