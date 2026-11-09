using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using MinecraftLauncher.Helpers;
using MinecraftLauncher.Models;
using MinecraftLauncher.Services;
using MinecraftLauncher.ViewModels;
using MinecraftLauncher.Views.Windows;

namespace MinecraftLauncher.Views.Pages
{
    public partial class SettingsPage : Page
    {
        // Локализация для строк, которые собираются в коде.
        private static readonly ILocalizationService _loc = LocalizationService.Instance;

        public SettingsViewModel ViewModel { get; }

        public SettingsPage()
        {
            InitializeComponent();
            ViewModel = new SettingsViewModel();
            DataContext = ViewModel;
            Unloaded += (s, e) => ViewModel.Cleanup();
        }

        /// <summary>
        /// Прокручивает страницу к списку аккаунтов и ставит блок у верхнего
        /// края вьюпорта (BringIntoView ограничивается «минимальным сдвигом»,
        /// и блок оказывался в самом низу, требуя доскроллить вручную).
        /// Вызывается кнопкой «Менеджер аккаунтов» в селекторе на главной.
        /// </summary>
        public void ScrollToAccounts()
        {
            var target = AccountsSection;
            if (target is null) return;

            var scroll = FindVisualAncestor<ScrollViewer>(target);
            if (scroll is null || scroll.Content is not UIElement content)
            {
                target.BringIntoView();
                return;
            }

            try
            {
                var y = target.TransformToVisual(content).Transform(new Point(0, 0)).Y;
                scroll.ScrollToVerticalOffset(Math.Max(0, y - 10));
            }
            catch (InvalidOperationException)
            {
                // Элемент ещё не в дереве/макете — откатываемся на родной механизм.
                target.BringIntoView();
            }
        }

        private static TAncestor? FindVisualAncestor<TAncestor>(DependencyObject? start)
            where TAncestor : DependencyObject
        {
            while (start is not null)
            {
                if (start is TAncestor found) return found;
                start = VisualTreeHelper.GetParent(start);
            }

            return null;
        }

        /// <summary>
        /// PasswordBox не биндится обычной привязкой, поэтому пароль Ely.by
        /// подтягивается в ViewModel вручную на каждое нажатие клавиши.
        /// Пароль хранится только в памяти ViewModel и не уходит на диск.
        /// </summary>
        private void ElyPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (ViewModel is not null && sender is PasswordBox box)
            {
                ViewModel.ElyPasswordInput = box.Password;
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mainWindow)
            {
                mainWindow.CloseSettings();
            }
        }

        private void ExportSettings_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                FileName = "qlauncher_settings.json",
                Filter = "JSON Files (*.json)|*.json",
                Title = _loc.GetString("Str_Dialog_ExportSettings")
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    // Экспортируем копию без токенов: раньше сюда попадали живые
                    // access-токены Mojang/XBox, что превращало экспорт в готовый
                    // инструмент для кражи сессии (облако, флешка, "отправить другу").
                    var snapshot = SettingsRedactor.CreateSanitizedCopy(SettingsService.Instance.Settings);
                    string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(dlg.FileName, json);
                    ToastService.Instance.ShowSuccess(_loc.GetString("Str_Settings_Exported"), _loc.GetString("Str_T_Export"));
                }
                catch (Exception ex)
                {
                    ToastService.Instance.ShowError(_loc.Format("Str_Settings_ExportError", ex.Message), _loc.GetString("Str_T_Error"));
                }
            }
        }

        private void ImportSettings_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "JSON Files (*.json)|*.json",
                Title = _loc.GetString("Str_Dialog_ImportSettings")
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    string json = File.ReadAllText(dlg.FileName);
                    var settings = JsonSerializer.Deserialize<LauncherSettings>(json);
                    if (settings != null)
                    {
                        SettingsService.Instance.Save(settings);
                        ViewModel.LoadFromSettings();
                        ToastService.Instance.ShowSuccess(_loc.GetString("Str_Settings_Imported"), _loc.GetString("Str_T_ImportTitle"));
                    }
                }
                catch (Exception ex)
                {
                    ToastService.Instance.ShowError(_loc.Format("Str_Settings_ImportError", ex.Message), _loc.GetString("Str_T_Error"));
                }
            }
        }

        private void ResetSettings_Click(object sender, RoutedEventArgs e)
        {
            if (QMessageBoxWindow.Show(_loc.GetString("Str_Dialog_ResetSettings"), _loc.GetString("Str_Dialog_DefaultTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                var defaults = new LauncherSettings();
                SettingsService.Instance.Save(defaults);
                ViewModel.LoadFromSettings();
                ToastService.Instance.ShowInfo(_loc.GetString("Str_Settings_Reset"), _loc.GetString("Str_T_Reset"));
            }
        }
    }
}
