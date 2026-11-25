using Avalonia.Controls;
using Avalonia.Interactivity;
using MinecraftLauncher.Helpers;
using MinecraftLauncher.Models;
using MinecraftLauncher.ViewModels;

namespace MinecraftLauncher.Views.Pages
{
    public partial class ModpacksPage : UserControl
    {
        public ModpacksViewModel ViewModel { get; }

        public ModpacksPage()
        {
            InitializeComponent();
            ViewModel = new ModpacksViewModel();
            DataContext = ViewModel;

            ViewModel.RequestManageModpack += OnRequestManageModpack;
            ViewModel.RequestCreateModpack += OnRequestCreateModpack;
            ViewModel.RequestClose += OnRequestClose;
        }

        private void BackButton_Click(object? sender, RoutedEventArgs e)
        {
            if (UiHelper.MainWindow is MainWindow mainWin)
            {
                mainWin.AnimateGoBack();
            }
        }

        private void SelectPack_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string packName)
            {
                ViewModel.SelectPack(packName);
                if (UiHelper.MainWindow is MainWindow mainWin)
                {
                    mainWin.CloseSettings();
                }
            }
        }

        private void ManagePack_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string packName)
            {
                ViewModel.ManagePack(packName);
            }
        }

        private void CreatePack_Click(object? sender, RoutedEventArgs e) => ViewModel.CreatePack();

        private void ImportZip_Click(object? sender, RoutedEventArgs e) => ViewModel.ImportZip();

        private void OnRequestManageModpack(ModpackProfile pack)
        {
            if (UiHelper.MainWindow is MainWindow mainWin)
            {
                mainWin.AnimateNavigate(new ProfileManagerPage(pack));
            }
        }

        private void OnRequestCreateModpack()
        {
            if (UiHelper.MainWindow is MainWindow mainWin)
            {
                mainWin.CloseSettings();
                mainWin.OpenModpackOverlay();
            }
        }

        private void OnRequestClose()
        {
            if (UiHelper.MainWindow is MainWindow mainWin)
            {
                mainWin.CloseSettings();
            }
        }
    }
}
