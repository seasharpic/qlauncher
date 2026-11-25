using System;
using System.IO;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MinecraftLauncher.Helpers;
using MinecraftLauncher.Models;
using MinecraftLauncher.Services;
using MinecraftLauncher.ViewModels;

namespace MinecraftLauncher.Views.Pages
{
    public partial class SettingsPage : UserControl
    {
        private static readonly ILocalizationService _loc = LocalizationService.Instance;

        public SettingsViewModel ViewModel { get; }

        public SettingsPage()
        {
            InitializeComponent();
            ViewModel = new SettingsViewModel();
            DataContext = ViewModel;
            DetachedFromVisualTree += (_, _) => ViewModel.Cleanup();
        }

        public void ScrollToAccounts()
        {
            AccountsSection?.BringIntoView();
        }

        private void BackButton_Click(object? sender, RoutedEventArgs e) =>
            UiHelper.Navigator?.CloseSettings();

        private async void ExportSettings_Click(object? sender, RoutedEventArgs e)
        {
            var path = await UiHelper.SaveFileAsync(
                _loc.GetString("Str_Dialog_ExportSettings"),
                "qlauncher_settings.json",
                "json",
                new FilePickerFileType("JSON Files") { Patterns = new[] { "*.json" } });

            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    var snapshot = SettingsRedactor.CreateSanitizedCopy(SettingsService.Instance.Settings);
                    string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(path, json);
                    ToastService.Instance.ShowSuccess(_loc.GetString("Str_Settings_Exported"), _loc.GetString("Str_T_Export"));
                }
                catch (Exception ex)
                {
                    ToastService.Instance.ShowError(_loc.Format("Str_Settings_ExportError", ex.Message), _loc.GetString("Str_T_Error"));
                }
            }
        }

        private async void ImportSettings_Click(object? sender, RoutedEventArgs e)
        {
            var path = await UiHelper.PickFileAsync(
                _loc.GetString("Str_Dialog_ImportSettings"),
                new FilePickerFileType("JSON Files") { Patterns = new[] { "*.json" } });

            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
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
    }
}
