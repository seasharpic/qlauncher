using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MinecraftLauncher.Helpers;
using MinecraftLauncher.ViewModels;

namespace MinecraftLauncher.Views.Pages
{
    public partial class ScreenshotsPage : UserControl
    {
        public ScreenshotsViewModel ViewModel { get; }

        public ScreenshotsPage() : this("") { }

        public ScreenshotsPage(string gamePath)
        {
            InitializeComponent();
            ViewModel = new ScreenshotsViewModel(gamePath);
            DataContext = ViewModel;
        }

        private void BackButton_Click(object? sender, RoutedEventArgs e) =>
            UiHelper.Navigator?.CloseSettings();

        private void PreviewBackdrop_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.Source == sender)
            {
                ViewModel.ClosePreviewCommand.Execute(null);
            }
        }
    }
}
