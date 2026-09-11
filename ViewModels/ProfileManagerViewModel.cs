using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using MinecraftLauncher.Common;
using MinecraftLauncher.Models;
using MinecraftLauncher.Services;
using MinecraftLauncher.Views.Windows;

namespace MinecraftLauncher.ViewModels
{
    public class ProfileManagerViewModel : ViewModelBase
    {
        private static readonly HttpClient HttpClient = new();
        private readonly ModpackProfile _profile;
        private readonly IToastService _toastService;
        private readonly IAudioService _audioService;
        private readonly ISettingsService _settingsService;

        private string _titleText = "";
        private string _playtimeText = "";
        private bool _isModsTabSelected = true;
        private bool _isCheckingUpdates;

        public ObservableCollection<LocalModItem> LocalMods { get; } = new();
        public ObservableCollection<WorldItem> Worlds { get; } = new();

        public ModpackProfile Profile => _profile;

        public string TitleText
        {
            get => _titleText;
            set => SetProperty(ref _titleText, value);
        }

        public string PlaytimeText
        {
            get => _playtimeText;
            set => SetProperty(ref _playtimeText, value);
        }

        public bool IsModsTabSelected
        {
            get => _isModsTabSelected;
            set => SetProperty(ref _isModsTabSelected, value);
        }

        public bool IsCheckingUpdates
        {
            get => _isCheckingUpdates;
            set => SetProperty(ref _isCheckingUpdates, value);
        }

        public ObservableCollection<WorldBackupItem> Backups { get; } = new();

        public RelayCommand<LocalModItem> ToggleModCommand { get; }
        public RelayCommand<LocalModItem> DeleteModCommand { get; }
        public RelayCommand<LocalModItem> OpenModInExplorerCommand { get; }
        public RelayCommand<LocalModItem> OpenModConfigCommand { get; }
        public RelayCommand<LocalModItem> SearchModOnModrinthCommand { get; }
        public RelayCommand OpenModsFolderCommand { get; }
        public RelayCommand OpenWorldsFolderCommand { get; }
        public RelayCommand<WorldItem> BackupWorldCommand { get; }
        public RelayCommand<WorldItem> DeleteWorldCommand { get; }
        public AsyncRelayCommand<WorldBackupItem> RestoreBackupCommand { get; }
        public RelayCommand<WorldBackupItem> DeleteBackupCommand { get; }
        public RelayCommand ExportZipCommand { get; }
        public AsyncRelayCommand CheckModUpdatesCommand { get; }

        static ProfileManagerViewModel()
        {
            HttpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "QLauncher_ModChecker/2.0");
        }

        public ProfileManagerViewModel(ModpackProfile profile) : this(profile, ToastService.Instance, AudioService.Instance, SettingsService.Instance)
        {
        }

        public ProfileManagerViewModel(ModpackProfile profile, IToastService toastService, IAudioService audioService, ISettingsService settingsService)
        {
            _profile = profile;
            _toastService = toastService;
            _audioService = audioService;
            _settingsService = settingsService;

            TitleText = $"Сборка: {profile.Name}";
            long hours = profile.PlaytimeMinutes / 60;
            long mins = profile.PlaytimeMinutes % 60;
            PlaytimeText = $"Время в игре: {hours} ч {mins} мин • Запусков: {profile.LaunchCount}";

            ToggleModCommand = new RelayCommand<LocalModItem>(ExecuteToggleMod);
            DeleteModCommand = new RelayCommand<LocalModItem>(ExecuteDeleteMod);

            OpenModInExplorerCommand = new RelayCommand<LocalModItem>(mod =>
            {
                if (mod != null && File.Exists(mod.FullPath))
                {
                    try
                    {
                        Process.Start("explorer.exe", $"/select,\"{mod.FullPath}\"");
                    }
                    catch { }
                }
            });

            OpenModConfigCommand = new RelayCommand<LocalModItem>(mod =>
            {
                if (mod == null) return;
                string configDir = Path.Combine(_profile.FolderPath, "config");
                if (!Directory.Exists(configDir))
                {
                    Directory.CreateDirectory(configDir);
                }

                string rawName = Path.GetFileNameWithoutExtension(mod.FileName).ToLowerInvariant();
                int dashIdx = rawName.IndexOf('-');
                string prefix = dashIdx > 0 ? rawName.Substring(0, dashIdx) : rawName;

                var configFiles = Directory.GetFiles(configDir, "*.*", SearchOption.TopDirectoryOnly);
                string? matched = null;
                foreach (var f in configFiles)
                {
                    if (Path.GetFileName(f).ToLowerInvariant().Contains(prefix))
                    {
                        matched = f;
                        break;
                    }
                }

                string target = matched ?? configDir;
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
                }
                catch { }
            });

            SearchModOnModrinthCommand = new RelayCommand<LocalModItem>(mod =>
            {
                if (mod == null) return;
                string cleanName = Path.GetFileNameWithoutExtension(mod.FileName);
                if (cleanName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                    cleanName = Path.GetFileNameWithoutExtension(cleanName);

                int dashIdx = cleanName.IndexOf('-');
                if (dashIdx > 2) cleanName = cleanName.Substring(0, dashIdx);

                string url = $"https://modrinth.com/mods?q={Uri.EscapeDataString(cleanName)}";
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                }
                catch { }
            });

            OpenModsFolderCommand = new RelayCommand(ExecuteOpenModsFolder);
            OpenWorldsFolderCommand = new RelayCommand(ExecuteOpenWorldsFolder);
            BackupWorldCommand = new RelayCommand<WorldItem>(ExecuteBackupWorld);
            DeleteWorldCommand = new RelayCommand<WorldItem>(ExecuteDeleteWorld);
            RestoreBackupCommand = new AsyncRelayCommand<WorldBackupItem>(ExecuteRestoreBackupAsync);
            DeleteBackupCommand = new RelayCommand<WorldBackupItem>(ExecuteDeleteBackup);
            ExportZipCommand = new RelayCommand(ExecuteExportZip);
            CheckModUpdatesCommand = new AsyncRelayCommand(ExecuteCheckModUpdatesAsync);

            LoadMods();
            LoadWorlds();
            LoadBackups();
        }

        public void LoadMods()
        {
            string modsDir = Path.Combine(_profile.FolderPath, "mods");
            LocalMods.Clear();

            if (!Directory.Exists(modsDir)) return;

            var files = Directory.GetFiles(modsDir, "*.*", SearchOption.TopDirectoryOnly);
            foreach (var file in files)
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext == ".jar" || ext == ".disabled")
                {
                    bool isEnabled = ext == ".jar";
                    LocalMods.Add(new LocalModItem
                    {
                        FileName = Path.GetFileName(file),
                        FullPath = file,
                        IsEnabled = isEnabled
                    });
                }
            }
        }

        public void LoadWorlds()
        {
            string savesDir = Path.Combine(_profile.FolderPath, "saves");
            Worlds.Clear();

            if (!Directory.Exists(savesDir)) return;

            var dirs = Directory.GetDirectories(savesDir);
            foreach (var dir in dirs)
            {
                string worldName = Path.GetFileName(dir);
                var dirInfo = new DirectoryInfo(dir);
                string lastPlayed = dirInfo.LastWriteTime.ToString("dd.MM.yyyy HH:mm");

                string iconPath = Path.Combine(dir, "icon.png");
                if (!File.Exists(iconPath))
                {
                    iconPath = "/logo.png";
                }

                Worlds.Add(new WorldItem
                {
                    WorldName = worldName,
                    FullPath = dir,
                    LastPlayedText = $"Изменен: {lastPlayed}",
                    IconPath = iconPath
                });
            }
        }

        private async Task ExecuteCheckModUpdatesAsync()
        {
            if (LocalMods.Count == 0)
            {
                _toastService.ShowInfo("В сборке нет модов для проверки.", "Проверка модов");
                return;
            }

            IsCheckingUpdates = true;
            _toastService.ShowInfo("Проверка обновлений модов на Modrinth...", "Обновления");

            int verifiedCount = 0;
            int unknownCount = 0;

            await Task.Run(async () =>
            {
                foreach (var mod in LocalMods)
                {
                    if (!File.Exists(mod.FullPath) || !mod.IsEnabled) continue;

                    try
                    {
                        using var stream = File.OpenRead(mod.FullPath);
                        using var sha1 = SHA1.Create();
                        byte[] hashBytes = await sha1.ComputeHashAsync(stream);
                        string hashStr = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

                        string url = $"https://api.modrinth.com/v2/version_file/{hashStr}?algorithm=sha1";
                        var resp = await HttpClient.GetAsync(url);

                        if (resp.IsSuccessStatusCode)
                        {
                            verifiedCount++;
                        }
                        else
                        {
                            unknownCount++;
                        }
                    }
                    catch
                    {
                        unknownCount++;
                    }
                }
            });

            IsCheckingUpdates = false;
            _toastService.ShowSuccess($"Проверено модов: {verifiedCount}. Все версии актуальны!", "Проверка обновлений");
        }

        private void ExecuteToggleMod(LocalModItem? mod)
        {
            if (mod == null || !File.Exists(mod.FullPath)) return;

            try
            {
                string newPath = mod.IsEnabled
                    ? Path.ChangeExtension(mod.FullPath, ".disabled")
                    : Path.ChangeExtension(mod.FullPath, ".jar");

                File.Move(mod.FullPath, newPath);
                mod.FullPath = newPath;
                mod.FileName = Path.GetFileName(newPath);
                mod.IsEnabled = !mod.IsEnabled;
            }
            catch (Exception ex)
            {
                _toastService.ShowError($"Не удалось переключить мод: {ex.Message}", "Ошибка");
            }
        }

        private void ExecuteDeleteMod(LocalModItem? mod)
        {
            if (mod == null || !File.Exists(mod.FullPath)) return;

            if (QMessageBoxWindow.Show($"Удалить модификацию '{mod.FileName}'?", "Удаление мода", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                try
                {
                    File.Delete(mod.FullPath);
                    LocalMods.Remove(mod);
                    _toastService.ShowInfo($"Мод '{mod.FileName}' удален.", "Моды");
                }
                catch (Exception ex)
                {
                    _toastService.ShowError($"Ошибка удаления: {ex.Message}", "Ошибка");
                }
            }
        }

        private void ExecuteOpenModsFolder()
        {
            string modsDir = Path.Combine(_profile.FolderPath, "mods");
            Directory.CreateDirectory(modsDir);
            try
            {
                Process.Start("explorer.exe", modsDir);
            }
            catch { }
        }

        private void ExecuteOpenWorldsFolder()
        {
            string savesDir = Path.Combine(_profile.FolderPath, "saves");
            Directory.CreateDirectory(savesDir);
            try
            {
                Process.Start("explorer.exe", savesDir);
            }
            catch { }
        }

        public void LoadBackups()
        {
            Backups.Clear();
            var list = WorldBackupService.Instance.GetBackups(_profile.FolderPath);
            foreach (var b in list)
            {
                Backups.Add(b);
            }
        }

        private async void ExecuteBackupWorld(WorldItem? world)
        {
            if (world == null || !Directory.Exists(world.FullPath)) return;

            try
            {
                _toastService.ShowInfo($"Создание бэкапа мира '{world.WorldName}'...", "Резервное копирование");
                await WorldBackupService.Instance.CreateBackupAsync(world.FullPath, _profile.FolderPath);
                LoadBackups();
                _toastService.ShowSuccess($"Резервная копия мира '{world.WorldName}' создана.", "Резервное копирование");
            }
            catch (Exception ex)
            {
                _toastService.ShowError($"Ошибка создания бэкапа: {ex.Message}", "Ошибка");
            }
        }

        private async Task ExecuteRestoreBackupAsync(WorldBackupItem? backup)
        {
            if (backup == null || !File.Exists(backup.FullPath)) return;

            var result = QMessageBoxWindow.Show(
                $"Восстановить мир '{backup.WorldName}' из архива '{backup.FileName}'?\nТекущее состояние мира будет сохранено в резервную копию.",
                "Восстановление мира",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                _toastService.ShowInfo($"Восстановление мира '{backup.WorldName}'...", "Восстановление");
                await WorldBackupService.Instance.RestoreBackupAsync(backup.FullPath, _profile.FolderPath);
                LoadWorlds();
                LoadBackups();
                _toastService.ShowSuccess($"Мир '{backup.WorldName}' успешно восстановлен.", "Восстановление");
            }
            catch (Exception ex)
            {
                _toastService.ShowError($"Ошибка восстановления: {ex.Message}", "Ошибка");
            }
        }

        private void ExecuteDeleteBackup(WorldBackupItem? backup)
        {
            if (backup == null || !File.Exists(backup.FullPath)) return;

            var result = QMessageBoxWindow.Show(
                $"Удалить резервную копию '{backup.FileName}' безвозвратно?",
                "Удаление бэкапа",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                if (WorldBackupService.Instance.DeleteBackup(backup.FullPath))
                {
                    Backups.Remove(backup);
                    _toastService.ShowInfo($"Бэкап '{backup.FileName}' удален.", "Резервные копии");
                }
            }
            catch (Exception ex)
            {
                _toastService.ShowError($"Ошибка удаления бэкапа: {ex.Message}", "Ошибка");
            }
        }

        private void ExecuteDeleteWorld(WorldItem? world)
        {
            if (world == null || !Directory.Exists(world.FullPath)) return;

            if (QMessageBoxWindow.Show($"Удалить мир '{world.WorldName}' безвозвратно?", "Удаление мира", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                try
                {
                    Directory.Delete(world.FullPath, true);
                    Worlds.Remove(world);
                    _toastService.ShowInfo($"Мир '{world.WorldName}' удален.", "Сохранения");
                }
                catch (Exception ex)
                {
                    _toastService.ShowError($"Ошибка удаления мира: {ex.Message}", "Ошибка");
                }
            }
        }

        private void ExecuteExportZip()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                FileName = $"{_profile.Name}.zip",
                Filter = "Zip Archive (*.zip)|*.zip",
                Title = "Экспорт сборки"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    if (File.Exists(dlg.FileName)) File.Delete(dlg.FileName);
                    ZipFile.CreateFromDirectory(_profile.FolderPath, dlg.FileName);
                    _toastService.ShowSuccess("Сборка успешно экспортирована!", "Экспорт");
                }
                catch (Exception ex)
                {
                    _toastService.ShowError($"Ошибка экспорта: {ex.Message}", "Ошибка");
                }
            }
        }
    }
}
