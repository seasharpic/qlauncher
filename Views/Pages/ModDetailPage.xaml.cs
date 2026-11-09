using System.Windows;
using System.Windows.Controls;
using MinecraftLauncher.Models;
using MinecraftLauncher.ViewModels;

namespace MinecraftLauncher.Views.Pages
{
    /// <summary>
    /// Страница мода: описание, файлы и установка. Возвращает на результаты
    /// поиска через журнал MainFrame — состояние фильтров не теряется.
    /// </summary>
    public partial class ModDetailPage : Page
    {
        public ModDetailViewModel ViewModel { get; }

        public ModDetailPage(ModItem item, ModCategoryKind kind)
        {
            InitializeComponent();
            ViewModel = new ModDetailViewModel(item, kind);
            DataContext = ViewModel;
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mainWindow)
            {
                // Журнал MainFrame вернёт на ту же ModsPage с фильтрами и результатами.
                mainWindow.AnimateGoBack();
            }
        }
    }
}
