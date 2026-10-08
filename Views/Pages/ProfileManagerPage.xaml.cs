using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MinecraftLauncher.Models;
using MinecraftLauncher.Services;
using MinecraftLauncher.ViewModels;
using MinecraftLauncher.Views.Windows;

namespace MinecraftLauncher.Views.Pages
{
    public partial class ProfileManagerPage : Page
    {
        // Локализация для строк, которые собираются в коде.
        private static readonly ILocalizationService _loc = LocalizationService.Instance;

        private readonly ModpackProfile _profile;
        public ProfileManagerViewModel ViewModel { get; }

        public ProfileManagerPage(ModpackProfile profile)
        {
            InitializeComponent();
            _profile = profile;
            ViewModel = new ProfileManagerViewModel(profile);
            DataContext = ViewModel;
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mainWin)
            {
                mainWin.AnimateGoBack();
            }
            else if (NavigationService != null && NavigationService.CanGoBack)
            {
                NavigationService.GoBack();
            }
        }

        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (ModsView == null || WorldsView == null) return;

            bool isMods = TabModsRadio.IsChecked == true;
            Grid targetView = isMods ? ModsView : WorldsView;
            Grid oldView = isMods ? WorldsView : ModsView;

            oldView.Visibility = Visibility.Collapsed;
            targetView.Visibility = Visibility.Visible;

            var fadeIn = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            var transform = new TranslateTransform();
            targetView.RenderTransform = transform;

            var slideUp = new DoubleAnimation
            {
                From = 14.0,
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            targetView.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            transform.BeginAnimation(TranslateTransform.YProperty, slideUp);
        }

        private void CreatePack_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mainWin)
            {
                mainWin.CloseSettings();
                mainWin.OpenModpackOverlay();
            }
        }

        private void ExportPackZip_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ExportZipCommand.Execute(null);
        }

        private void CreateShortcut_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mainWin)
            {
                mainWin.OpenShortcutOverlay();
            }
        }

        private void DeletePack_Click(object sender, RoutedEventArgs e)
        {
            if (QMessageBoxWindow.Show(_loc.Format("Str_Dialog_DeletePack", _profile.Name), _loc.GetString("Str_Dialog_DefaultTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
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

                    if (Window.GetWindow(this) is MainWindow mainWin)
                    {
                        mainWin.ReloadLauncher();
                        mainWin.CloseSettings();
                    }
                    ToastService.Instance.ShowSuccess(_loc.GetString("Str_Profile_DeletedPlain"), _loc.GetString("Str_T_Success"));
                }
            }
        }
    }
}
