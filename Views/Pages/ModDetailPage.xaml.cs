using Avalonia.Controls;
using Avalonia.Interactivity;
using MinecraftLauncher.Helpers;
using MinecraftLauncher.Models;
using MinecraftLauncher.ViewModels;

namespace MinecraftLauncher.Views.Pages
{
    public partial class ModDetailPage : UserControl
    {
        public ModDetailViewModel ViewModel { get; }

        public ModDetailPage() : this(new ModItem(), ModCategoryKind.Mod) { }

        public ModDetailPage(ModItem item, ModCategoryKind kind)
        {
            InitializeComponent();
            ViewModel = new ModDetailViewModel(item, kind);
            DataContext = ViewModel;
        }

        private void BackButton_Click(object? sender, RoutedEventArgs e)
        {
            if (UiHelper.MainWindow is MainWindow mainWindow)
            {
                mainWindow.AnimateGoBack();
            }
        }
    }
}
