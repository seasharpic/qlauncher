using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MinecraftLauncher.Helpers;
using MinecraftLauncher.ViewModels;

namespace MinecraftLauncher.Views.Pages
{
    public partial class ModsPage : UserControl
    {
        public ModsViewModel ViewModel { get; }

        public ModsPage()
        {
            InitializeComponent();
            ViewModel = new ModsViewModel();
            DataContext = ViewModel;
        }

        private void BackButton_Click(object? sender, RoutedEventArgs e)
        {
            UiHelper.Navigator?.CloseSettings();
        }

        private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && ViewModel.SearchCommand.CanExecute(null))
            {
                ViewModel.SearchCommand.Execute(null);
            }
        }

        private void ModCard_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (e.InitialPressMouseButton == MouseButton.Left &&
                sender is Control element && element.DataContext is Models.ModItem item)
            {
                var kind = Models.ModCategoryKinds.FromKey(item.ProjectType);
                UiHelper.Navigator?.AnimateNavigate(new ModDetailPage(item, kind));
            }
        }
    }
}
