using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace MinecraftLauncher.Views.Controls
{
    public enum ToastType
    {
        Success,
        Error,
        Warning,
        Info
    }

    public partial class ToastNotification : UserControl
    {
        public ToastNotification()
        {
            InitializeComponent();
        }

        public async void Show(string title, string message, ToastType type = ToastType.Info, int durationMs = 3000)
        {
            TitleText.Text = title;
            MessageText.Text = message;

            switch (type)
            {
                case ToastType.Success:
                    IconText.Text = "✓";
                    IconText.Foreground = new SolidColorBrush(Color.FromRgb(80, 220, 100));
                    ToastBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(80, 220, 100));
                    break;
                case ToastType.Error:
                    IconText.Text = "✕";
                    IconText.Foreground = new SolidColorBrush(Color.FromRgb(255, 90, 90));
                    ToastBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 90, 90));
                    break;
                case ToastType.Warning:
                    IconText.Text = "⚠";
                    IconText.Foreground = new SolidColorBrush(Color.FromRgb(255, 195, 60));
                    ToastBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 195, 60));
                    break;
                case ToastType.Info:
                default:
                    IconText.Text = "ℹ";
                    if (Application.Current?.TryGetResource("AccentBrush", Application.Current.ActualThemeVariant, out var res) == true && res is IBrush accent)
                    {
                        IconText.Foreground = accent;
                        ToastBorder.BorderBrush = accent;
                    }
                    break;
            }

            ToastBorder.Opacity = 1;
            await Task.Delay(durationMs);
            ToastBorder.Opacity = 0;
            await Task.Delay(260);

            if (Parent is Panel parentPanel)
            {
                parentPanel.Children.Remove(this);
            }
        }
    }
}
