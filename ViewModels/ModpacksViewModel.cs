using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using System.Windows;
using MinecraftLauncher.Common;
using MinecraftLauncher.Helpers;
using MinecraftLauncher.Models;
using MinecraftLauncher.Services;
using MinecraftLauncher.Services.LaunchEngine;
using MinecraftLauncher.Views.Windows;

namespace MinecraftLauncher.ViewModels
{
    public class ModpacksViewModel : ViewModelBase
    {
        private readonly ISettingsService _settingsService;
        private readonly IToastService _toastService;

        // Локализация для строк, которые собираются в коде.
        private readonly ILocalizationService _loc = LocalizationService.Instance;
        private readonly IAudioService _audioService;

        public ObservableCollection<ModpackCardItem> Modpacks { get; } = new();

        public event Action<ModpackProfile>? RequestManageModpack;
        public event Action? RequestCreateModpack;
        public event Action? RequestClose;

        public RelayCommand<string> SelectPackCommand { get; }
        public RelayCommand<string> ManagePackCommand { get; }
        public RelayCommand<string> OpenPackFolderCommand { get; }
        public RelayCommand<string> OpenPackModsCommand { get; }
        public RelayCommand<string> DuplicatePackCommand { get; }
        public RelayCommand<string> CreateShortcutCommand { get; }
        public RelayCommand<string> DeletePackCommand { get; }

        public ModpacksViewModel() : this(SettingsService.Instance, ToastService.Instance, AudioService.Instance)
        {
        }

        public ModpacksViewModel(ISettingsService settingsService, IToastService toastService, IAudioService audioService)
        {
            _settingsService = settingsService;
            _toastService = toastService;
            _audioService = audioService;

            SelectPackCommand = new RelayCommand<string>(s => { if (s != null) SelectPack(s); });
            ManagePackCommand = new RelayCommand<string>(s => { if (s != null) ManagePack(s); });
            OpenPackFolderCommand = new RelayCommand<string>(ExecuteOpenPackFolder);
            OpenPackModsCommand = new RelayCommand<string>(ExecuteOpenPackMods);
            DuplicatePackCommand = new RelayCommand<string>(ExecuteDuplicatePack);
            CreateShortcutCommand = new RelayCommand<string>(ExecuteCreateShortcut);
            DeletePackCommand = new RelayCommand<string>(ExecuteDeletePack);

            LoadModpacks();
        }

        public void LoadModpacks()
        {
            var settings = _settingsService.Load();
            Modpacks.Clear();

            string currentActive = settings.LastSelectedVersion ?? "";

            foreach (var pack in settings.Modpacks)
            {
                string packTag = $"⭐ {pack.Name} ({pack.Loader})";
                bool isActive = currentActive == packTag || currentActive.StartsWith($"⭐ {pack.Name}");

                long hours = pack.PlaytimeMinutes / 60;
                long mins = pack.PlaytimeMinutes % 60;

                Modpacks.Add(new ModpackCardItem
                {
                    Name = pack.Name,
                    LoaderTag = string.IsNullOrWhiteSpace(pack.Loader) ? "Vanilla" : pack.Loader,
                    GameVersionTag = string.IsNullOrWhiteSpace(pack.GameVersion) ? "1.20.1" : pack.GameVersion,
                    PlaytimeText = _loc.Format("Str_Playtime_Summary", hours, mins, pack.LaunchCount),
                    IsActiveVisibility = isActive ? Visibility.Visible : Visibility.Collapsed
                });
            }
        }

        public void SelectPack(string packName)
        {
            var settings = _settingsService.Settings;
            var pack = settings.Modpacks.Find(p => p.Name == packName);
            if (pack != null)
            {
                string packTag = $"⭐ {pack.Name} ({pack.Loader})";
                settings.LastSelectedVersion = packTag;
                _settingsService.Save(settings);

                LoadModpacks();
                _toastService.ShowSuccess(_loc.Format("Str_Pack_Switched", pack.Name), _loc.GetString("Str_T_PackCreateTitle"));
                RequestClose?.Invoke();
            }
        }

        public void ManagePack(string packName)
        {
            _audioService.PlayClickSound(_settingsService.Settings.EnableUiSounds);
            var settings = _settingsService.Settings;
            var pack = settings.Modpacks.Find(p => p.Name == packName);
            if (pack != null)
            {
                RequestManageModpack?.Invoke(pack);
            }
        }

        public void CreatePack()
        {
            RequestCreateModpack?.Invoke();
        }

        public async void ImportZip()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Modpack files (*.mrpack;*.zip)|*.mrpack;*.zip|Modrinth Pack (*.mrpack)|*.mrpack|Zip Archive (*.zip)|*.zip",
                Title = _loc.GetString("Str_Dialog_ImportPackMinecraft")
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    _toastService.ShowInfo(_loc.GetString("Str_Import_Progress"), _loc.GetString("Str_T_ImportTitle"));

                    // Логика импорта вынесена в общий сервис: раньше она была
                    // продублирована в трёх местах и уже разошлась между ними.
                    var profile = await ModpackImportService.Instance.ImportAsync(
                        dlg.FileName,
                        _settingsService.Settings.GamePath);

                    // Изменение общего списка и сохранение — на UI-потоке.
                    _settingsService.Settings.Modpacks.Add(profile);
                    _settingsService.Save();

                    LoadModpacks();
                    _toastService.ShowSuccess(
                        _loc.Format("Str_Import_DoneWithLoader", profile.Name, profile.Loader),
                        _loc.GetString("Str_T_ImportTitle"));
                }
                catch (Exception ex)
                {
                    CrashLogWriter.Write("ModpackImport", $"Failed to import '{dlg.FileName}'", ex);
                    _toastService.ShowError(_loc.Format("Str_Import_Error", ex.Message), _loc.GetString("Str_T_Error"));
                }
            }
        }

        private void ExecuteOpenPackFolder(string? packName)
        {
            if (string.IsNullOrWhiteSpace(packName)) return;
            var pack = _settingsService.Settings.Modpacks.Find(p => p.Name == packName);
            if (pack != null && Directory.Exists(pack.FolderPath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = pack.FolderPath, UseShellExecute = true });
                }
                catch { }
            }
        }

        private void ExecuteOpenPackMods(string? packName)
        {
            if (string.IsNullOrWhiteSpace(packName)) return;
            var pack = _settingsService.Settings.Modpacks.Find(p => p.Name == packName);
            if (pack != null)
            {
                string modsDir = Path.Combine(pack.FolderPath, "mods");
                Directory.CreateDirectory(modsDir);
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = modsDir, UseShellExecute = true });
                }
                catch { }
            }
        }

        private async void ExecuteDuplicatePack(string? packName)
        {
            if (string.IsNullOrWhiteSpace(packName)) return;
            var pack = _settingsService.Settings.Modpacks.Find(p => p.Name == packName);
            if (pack == null || !Directory.Exists(pack.FolderPath)) return;

            try
            {
                string newName = $"{pack.Name}{_loc.GetString("Str_Profile_CopySuffix")}";
                string instancesDir = Path.Combine(_settingsService.Settings.GamePath, "instances");
                string newFolder = Path.Combine(instancesDir, newName);
                int counter = 2;
                while (Directory.Exists(newFolder) || _settingsService.Settings.Modpacks.Exists(p => p.Name == newName))
                {
                    newName = $"{pack.Name}{_loc.GetString("Str_Profile_CopySuffix")} {counter++}";
                    newFolder = Path.Combine(instancesDir, newName);
                }

                // Копирование всей сборки выполнялось на UI-потоке в синхронном RelayCommand:
                // сборка на несколько гигабайт подвешивала интерфейс на минуты.
                Directory.CreateDirectory(newFolder);
                await Task.Run(() => CopyDirectory(pack.FolderPath, newFolder));

                var newProfile = new ModpackProfile
                {
                    Name = newName,
                    GameVersion = pack.GameVersion,
                    Version = pack.Version,
                    Loader = pack.Loader,
                    FolderPath = newFolder
                };

                var settings = _settingsService.Settings;
                settings.Modpacks.Add(newProfile);
                _settingsService.Save(settings);

                LoadModpacks();
                _toastService.ShowSuccess(_loc.Format("Str_Pack_Duplicated", newName), _loc.GetString("Str_T_Clone"));
            }
            catch (Exception ex)
            {
                _toastService.ShowError(_loc.Format("Str_Pack_DuplicateError", ex.Message), _loc.GetString("Str_T_Error"));
            }
        }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            var dir = new DirectoryInfo(sourceDir);
            Directory.CreateDirectory(destinationDir);

            foreach (FileInfo file in dir.GetFiles())
            {
                string targetFilePath = Path.Combine(destinationDir, file.Name);
                file.CopyTo(targetFilePath, true);
            }

            foreach (DirectoryInfo subDir in dir.GetDirectories())
            {
                string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
                CopyDirectory(subDir.FullName, newDestinationDir);
            }
        }

        private void ExecuteCreateShortcut(string? packName)
        {
            if (string.IsNullOrWhiteSpace(packName)) return;
            var pack = _settingsService.Settings.Modpacks.Find(p => p.Name == packName);
            if (pack == null) return;

            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string shortcutFile = Path.Combine(desktop, $"Minecraft ({pack.Name}).lnk");
                string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";

                if (!string.IsNullOrEmpty(exePath))
                {
                    Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                    if (shellType != null)
                    {
                        dynamic shell = Activator.CreateInstance(shellType)!;
                        dynamic shortcut = shell.CreateShortcut(shortcutFile);
                        shortcut.TargetPath = exePath;
                        string packTag = $"⭐ {pack.Name} ({pack.Loader})";
                        shortcut.Arguments = $"-quickplay \"{packTag}\"";
                        shortcut.IconLocation = $"{exePath},0";
                        shortcut.WorkingDirectory = Path.GetDirectoryName(exePath) ?? "";
                        shortcut.Save();

                        _toastService.ShowSuccess(_loc.Format("Str_Pack_ShortcutCreated", pack.Name), _loc.GetString("Str_T_Shortcut"));
                    }
                }
            }
            catch (Exception ex)
            {
                _toastService.ShowError(_loc.Format("Str_Pack_ShortcutError", ex.Message), _loc.GetString("Str_T_Error"));
            }
        }

        private void ExecuteDeletePack(string? packName)
        {
            if (string.IsNullOrWhiteSpace(packName)) return;
            var pack = _settingsService.Settings.Modpacks.Find(p => p.Name == packName);
            if (pack == null) return;

            if (QMessageBoxWindow.Show(_loc.Format("Str_Pack_DeleteQuestion", pack.Name), _loc.GetString("Str_T_DeleteConfirm"), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                try
                {
                    if (Directory.Exists(pack.FolderPath))
                    {
                        Directory.Delete(pack.FolderPath, true);
                    }
                }
                catch { }

                var settings = _settingsService.Settings;
                settings.Modpacks.Remove(pack);
                _settingsService.Save(settings);

                LoadModpacks();
                _toastService.ShowInfo(_loc.Format("Str_Pack_DeleteDone", pack.Name), _loc.GetString("Str_T_Success"));
            }
        }
    }
}
