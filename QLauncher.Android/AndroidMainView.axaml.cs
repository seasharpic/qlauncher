using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MinecraftLauncher.Helpers;
using MinecraftLauncher.Services;
using MinecraftLauncher.ViewModels;
using MinecraftLauncher.Views.Pages;
using MinecraftLauncher.Views.Windows;

namespace MinecraftLauncher.Views
{
    public partial class AndroidMainView : UserControl, ILauncherNavigator
    {
        public MainViewModel ViewModel { get; }
        private readonly Stack<UserControl> _navHistory = new();
        private TaskCompletionSource<QMessageBoxResult>? _dialogTcs;
        private QMessageBoxButton _currentButtons;

        public AndroidMainView()
        {
            InitializeComponent();
            UiHelper.Navigator = this;
            QMessageBoxWindow.Handler = ShowDialogOverlayAsync;

            ViewModel = new MainViewModel();
            DataContext = ViewModel;

            Loaded += async (_, _) =>
            {
                ToastService.Instance.RegisterContainer(ToastContainer);
                try
                {
                    await ViewModel.InitializeAsync();
                }
                catch (Exception ex)
                {
                    ToastService.Instance.ShowError(ex.Message, "Str_T_Error");
                }
            };
        }

        private Task<QMessageBoxResult> ShowDialogOverlayAsync(
            string message, string? title, QMessageBoxButton buttons, QMessageBoxImage image)
        {
            _dialogTcs = new TaskCompletionSource<QMessageBoxResult>();
            _currentButtons = buttons;
            DialogTitle.Text = title ?? "QLauncher";
            DialogMessage.Text = message;
            DialogNoBtn.IsVisible = buttons == QMessageBoxButton.YesNo || buttons == QMessageBoxButton.OKCancel;
            DialogYesBtn.Content = buttons == QMessageBoxButton.YesNo
                ? LocalizationService.Instance.GetString("Str_Yes")
                : LocalizationService.Instance.GetString("Str_Ok");
            DialogOverlay.IsVisible = true;
            return _dialogTcs.Task;
        }

        private void DialogYes_Click(object? sender, RoutedEventArgs e)
        {
            DialogOverlay.IsVisible = false;
            _dialogTcs?.TrySetResult(_currentButtons == QMessageBoxButton.YesNo ? QMessageBoxResult.Yes : QMessageBoxResult.OK);
        }

        private void DialogNo_Click(object? sender, RoutedEventArgs e)
        {
            DialogOverlay.IsVisible = false;
            _dialogTcs?.TrySetResult(_currentButtons == QMessageBoxButton.YesNo ? QMessageBoxResult.No : QMessageBoxResult.Cancel);
        }

        public void AnimateNavigate(UserControl page)
        {
            if (MainFrame.IsVisible && MainFrame.Content is UserControl current)
                _navHistory.Push(current);

            HomeView.IsVisible = false;
            MainFrame.Content = page;
            MainFrame.IsVisible = true;
        }

        public void AnimateGoBack()
        {
            if (_navHistory.Count > 0)
                MainFrame.Content = _navHistory.Pop();
            else
                CloseSettings();
        }

        public void CloseSettings()
        {
            _navHistory.Clear();
            MainFrame.IsVisible = false;
            MainFrame.Content = null;
            HomeView.IsVisible = true;
            _ = ViewModel.LoadAccountsAsync();
            _ = ViewModel.LoadVersionsAsync();
        }

        public void OpenModpackOverlay() { }
        public void OpenShortcutOverlay() { }

        public void ReloadLauncher()
        {
            _ = ViewModel.LoadAccountsAsync();
            _ = ViewModel.LoadVersionsAsync();
        }

        private void NavHome_Click(object? sender, RoutedEventArgs e) => CloseSettings();
        private void NavModpacks_Click(object? sender, RoutedEventArgs e) => AnimateNavigate(new ModpacksPage());
        private void NavMods_Click(object? sender, RoutedEventArgs e) => AnimateNavigate(new ModsPage());
        private void NavSettings_Click(object? sender, RoutedEventArgs e) => AnimateNavigate(new SettingsPage());

        private void OpenAmethyst_Click(object? sender, RoutedEventArgs e)
        {
            if (AndroidLauncherBridge.OpenInAmethystHandler == null || !AndroidLauncherBridge.OpenInAmethystHandler())
            {
                ToastService.Instance.ShowWarning(
                    LocalizationService.Instance.GetString("Str_Android_AmethystMissing"),
                    "Str_T_Warning");
            }
        }
    }

    public static class AndroidLauncherBridge
    {
        public static Func<bool>? OpenInAmethystHandler { get; set; }
    }
}
