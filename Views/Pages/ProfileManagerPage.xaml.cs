using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MinecraftLauncher.Helpers;
using MinecraftLauncher.Models;
using MinecraftLauncher.Services;
using MinecraftLauncher.ViewModels;
using MinecraftLauncher.Views.Windows;

namespace MinecraftLauncher.Views.Pages
{
    public partial class ProfileManagerPage : UserControl
    {
        private static readonly ILocalizationService _loc = LocalizationService.Instance;
        private readonly ModpackProfile _profile;
        public ProfileManagerViewModel ViewModel { get; }

        public ProfileManagerPage() : this(new ModpackProfile()) { }

        public ProfileManagerPage(ModpackProfile profile)
        {
            InitializeComponent();
            _profile = profile;
            ViewModel = new ProfileManagerViewModel(profile);
            DataContext = ViewModel;
        }

        private void BackButton_Click(object? sender, RoutedEventArgs e) =>
            UiHelper.Navigator?.AnimateGoBack();

        private void Tab_Checked(object? sender, RoutedEventArgs e)
        {
            if (ModsView == null || WorldsView == null) return;
            bool isMods = TabModsRadio.IsChecked == true;
            ModsView.IsVisible = isMods;
            WorldsView.IsVisible = !isMods;
        }

        private void CreatePack_Click(object? sender, RoutedEventArgs e)
        {
            UiHelper.Navigator?.CloseSettings();
            UiHelper.Navigator?.OpenModpackOverlay();
        }

        private void CreateShortcut_Click(object? sender, RoutedEventArgs e) =>
            UiHelper.Navigator?.OpenShortcutOverlay();

        private async void DeletePack_Click(object? sender, RoutedEventArgs e)
        {
            if (await QMessageBoxWindow.ShowAsync(_loc.Format("Str_Dialog_DeletePack", _profile.Name), _loc.GetString("Str_Dialog_DefaultTitle"), QMessageBoxButton.YesNo, QMessageBoxImage.Warning) == QMessageBoxResult.Yes)
            {
                var settings = SettingsService.Instance.Settings;
                var modpack = settings.Modpacks.Find(p => p.Name == _profile.Name);
                if (modpack != null)
                {
                    try
                    {
                        if (Directory.Exists(modpack.FolderPath))
                        {
                            Directory.Delete(modpack.FolderPath, true);
                        }
                    }
                    catch { }

                    settings.Modpacks.Remove(modpack);
                    SettingsService.Instance.Save(settings);

                    UiHelper.Navigator?.ReloadLauncher();
                    UiHelper.Navigator?.CloseSettings();
                    ToastService.Instance.ShowSuccess(_loc.GetString("Str_Profile_DeletedPlain"), _loc.GetString("Str_T_Success"));
                }
            }
        }
    }
}
