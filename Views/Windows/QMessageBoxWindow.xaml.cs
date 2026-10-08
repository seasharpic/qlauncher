using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using MinecraftLauncher.Services;

namespace MinecraftLauncher.Views.Windows
{
    public partial class QMessageBoxWindow : Window
    {
        // Локализация для подписей кнопок и заголовка по умолчанию.
        private static readonly ILocalizationService L = LocalizationService.Instance;

        public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

        public QMessageBoxWindow(string message, string? title, MessageBoxButton buttons, MessageBoxImage image)
        {
            InitializeComponent();

            MessageText.Text = message;
            TitleText.Text = string.IsNullOrEmpty(title)
                ? L.GetString("Str_MessageBox_WindowTitle")
                : title;

            if (image == MessageBoxImage.Error)
                TopAccent.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E81123"));
            else if (image == MessageBoxImage.Warning)
                TopAccent.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F5A623"));
            else
                TopAccent.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B85E6"));

            if (buttons == MessageBoxButton.YesNo)
            {
                // Подписи кнопок берём из ресурсов: раньше они были русскими
                // литералами, и в английском интерфейсе диалог подтверждения
                // оставался смешанным.
                AddButton(L.GetString("Str_Yes"), MessageBoxResult.Yes, "#3B85E6", true);
                AddButton(L.GetString("Str_No"), MessageBoxResult.No, "#3A3D4D", false);
            }
            else
            {
                AddButton(L.GetString("Str_Ok"), MessageBoxResult.OK, "#3B85E6", true);
            }
        }

        private void AddButton(string text, MessageBoxResult result, string colorHex, bool isPrimary)
        {
            var btn = new Button
            {
                Content = text,
                Width = 90,
                Height = 35,
                Margin = new Thickness(10, 0, 0, 0),
                Foreground = Brushes.White,
                Cursor = System.Windows.Input.Cursors.Hand,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex)),
                BorderThickness = new Thickness(0),
                Template = CreateButtonTemplate(isPrimary ? 6 : 4)
            };

            btn.Click += (_, _) =>
            {
                Result = result;
                Close();
            };

            ButtonsPanel.Children.Add(btn);
        }

        private static ControlTemplate CreateButtonTemplate(int cornerRadius)
        {
            string xaml = $@"
                <ControlTemplate TargetType='Button' xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
                    <Border Background='{{TemplateBinding Background}}' CornerRadius='{cornerRadius}'>
                        <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
                    </Border>
                </ControlTemplate>";
            return (ControlTemplate)XamlReader.Parse(xaml);
        }

        public static MessageBoxResult Show(string message, string title = null!, MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.Information)
        {
            var msgBox = new QMessageBoxWindow(message, title, buttons, image);

            if (Application.Current?.MainWindow != null && Application.Current.MainWindow.IsVisible)
            {
                msgBox.Owner = Application.Current.MainWindow;
            }

            msgBox.ShowDialog();
            return msgBox.Result;
        }
    }
}
