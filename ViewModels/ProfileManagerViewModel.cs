using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using MinecraftLauncher.Common;
using MinecraftLauncher.Helpers;
using MinecraftLauncher.Models;
using MinecraftLauncher.Services;
using MinecraftLauncher.Views.Windows;

namespace MinecraftLauncher.ViewModels
{
    public class ProfileManagerViewModel : ViewModelBase
    {
        private readonly ModpackProfile _profile;
        private readonly IToastService _toastService;

        // Локализация для строк, которые собираются в коде.
        private readonly ILocalizationService _loc = LocalizationService.Instance;
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

        private bool _hasPendingUpdates;

        /// <summary>Проверка нашла обновления: показывает кнопку «Обновить всё».</summary>
        public bool HasPendingUpdates
        {
            get => _hasPendingUpdates;
            set => SetProperty(ref _hasPendingUpdates, value);
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
        public AsyncRelayCommand UpdateAllModsCommand { get; }
        public AsyncRelayCommand<LocalModItem> UpdateOneModCommand { get; }
        public AsyncRelayCommand CreateModsBackupCommand { get; }

        /// <summary>Обновления, найденные последней проверкой: OldPath → кандидат.</summary>
        private List<ModUpdateCandidate> _pendingUpdates = new();

        public ProfileManagerViewModel(ModpackProfile profile) : this(profile, ToastService.Instance, AudioService.Instance, SettingsService.Instance)
        {
        }

        public ProfileManagerViewModel(ModpackProfile profile, IToastService toastService, IAudioService audioService, ISettingsService settingsService)
        {
            _profile = profile;
            _toastService = toastService;
            _audioService = audioService;
            _settingsService = settingsService;

            TitleText = _loc.Format("Str_Profile_Title", profile.Name);
            long hours = profile.PlaytimeMinutes / 60;
            long mins = profile.PlaytimeMinutes % 60;
            PlaytimeText = _loc.Format("Str_Profile_Playtime", hours, mins, profile.LaunchCount);

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
            UpdateAllModsCommand = new AsyncRelayCommand(ExecuteUpdateAllModsAsync);
            UpdateOneModCommand = new AsyncRelayCommand<LocalModItem>(ExecuteUpdateOneModAsync);
            CreateModsBackupCommand = new AsyncRelayCommand(ExecuteCreateModsBackupAsync);

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
                    LastPlayedText = _loc.Format("Str_Profile_LastPlayed", lastPlayed),
                    IconPath = iconPath
                });
            }
        }

        private async Task ExecuteCheckModUpdatesAsync()
        {
            if (LocalMods.Count == 0)
            {
                _toastService.ShowInfo(_loc.GetString("Str_Mods_NothingToCheck"), _loc.GetString("Str_T_ModsCheck"));
                return;
            }

            IsCheckingUpdates = true;

            try
            {
                // Сброс прошлых бейджей до скана: старая галка не должна дожить
                // до новой проверки.
                foreach (var item in LocalMods)
                {
                    item.HasUpdate = false;
                    item.UpdateText = "";
                }

                string modsFolder = Path.Combine(_profile.FolderPath, "mods");
                var scan = await ModUpdateService.ScanAsync(
                    modsFolder, _profile.GameVersion, _profile.Loader).ConfigureAwait(true);

                _pendingUpdates = scan.Updates;
                HasPendingUpdates = scan.Updates.Count > 0;

                foreach (var candidate in scan.Updates)
                {
                    var item = LocalMods.FirstOrDefault(m =>
                        string.Equals(
                            Path.GetFullPath(m.FullPath),
                            Path.GetFullPath(candidate.OldPath),
                            StringComparison.OrdinalIgnoreCase));
                    if (item == null) continue; // Jar перезаписан при скане — пропускаем.

                    item.HasUpdate = true;
                    item.UpdateText = _loc.Format("Str_Mods_RowUpdate", candidate.FreshName);
                }

                foreach (string unknown in scan.UnknownPaths)
                {
                    var item = LocalMods.FirstOrDefault(m =>
                        string.Equals(
                            Path.GetFullPath(m.FullPath),
                            Path.GetFullPath(unknown),
                            StringComparison.OrdinalIgnoreCase));
                    if (item == null) continue;
                    item.UpdateText = _loc.GetString("Str_Mods_RowUnknown");
                }

                if (scan.Updates.Count > 0)
                {
                    _toastService.ShowInfo(
                        _loc.Format("Str_Mods_UpdateFound", scan.Updates.Count),
                        _loc.GetString("Str_T_Updates"));
                }
                else if (scan.UnknownPaths.Count > 0)
                {
                    _toastService.ShowInfo(
                        _loc.Format("Str_Mods_Partial", scan.ScannedCount - scan.UnknownPaths.Count, scan.UnknownPaths.Count),
                        _loc.GetString("Str_T_Updates"));
                }
                else
                {
                    _toastService.ShowSuccess(
                        _loc.Format("Str_Mods_AllCurrent", scan.ScannedCount),
                        _loc.GetString("Str_T_Updates"));
                }
            }
            catch (Exception ex)
            {
                CrashLogWriter.Write("ProfileManagerViewModel", "Update check failed", ex);
                _toastService.ShowError(
                    _loc.Format("Str_Mods_DownloadError", ex.Message),
                    _loc.GetString("Str_T_Error"));
            }
            finally
            {
                IsCheckingUpdates = false;
            }
        }

        private async Task ExecuteUpdateOneModAsync(LocalModItem? item)
        {
            if (item == null) return;

            var candidate = _pendingUpdates.FirstOrDefault(c =>
                string.Equals(
                    Path.GetFullPath(c.OldPath),
                    Path.GetFullPath(item.FullPath),
                    StringComparison.OrdinalIgnoreCase));
            if (candidate == null) return;

            IsCheckingUpdates = true;
            try
            {
                // Замена jar — необратимая операция: сначала бэкап.
                if (!await TryBackupBeforeReplaceAsync(announceAuto: true).ConfigureAwait(true))
                {
                    return;
                }
                bool updated = await ApplyUpdateAsync(candidate).ConfigureAwait(true);
                if (updated)
                {
                    LoadMods();
                }
            }
            finally
            {
                IsCheckingUpdates = false;
            }
        }

        private async Task ExecuteUpdateAllModsAsync()
        {
            if (_pendingUpdates.Count == 0)
            {
                _toastService.ShowInfo(_loc.GetString("Str_Mods_NothingToCheck"), _loc.GetString("Str_T_Updates"));
                return;
            }

            IsCheckingUpdates = true;
            int done = 0;
            int failed = 0;
            try
            {
                // Прежде чем трогать хотя бы один jar — архив модов.
                if (!await TryBackupBeforeReplaceAsync(announceAuto: true).ConfigureAwait(true))
                {
                    return;
                }
                foreach (var candidate in _pendingUpdates.ToList())
                {
                    if (await ApplyUpdateAsync(candidate).ConfigureAwait(true))
                    {
                        done++;
                    }
                    else
                    {
                        failed++;
                    }
                }

                if (done > 0)
                {
                    LoadMods();
                }

                _toastService.ShowInfo(
                    _loc.Format("Str_Mods_UpdatedCount", done, failed),
                    _loc.GetString("Str_T_Updates"));
                HasPendingUpdates = _pendingUpdates.Count > 0;
            }
            finally
            {
                IsCheckingUpdates = false;
            }
        }

        /// <summary>
        /// Запускает процесс «модам будет больно»: сначала архив модов
        /// экземпляра. Возвращает false, если бэкап не удался, а юзер
        /// отказался продолжать.
        /// </summary>
        private async Task<bool> TryBackupBeforeReplaceAsync(bool announceAuto)
        {
            try
            {
                string? path = await InstanceBackupService.CreateModsBackupAsync(
                    _profile.FolderPath, _profile.Name, _profile.GameVersion, _profile.Loader)
                    .ConfigureAwait(true);

                if (path == null)
                {
                    // Модов нет — заменять нечего, бэкап не требуется.
                    return true;
                }

                _toastService.ShowSuccess(
                    _loc.Format(announceAuto ? "Str_Backup_Mods_Auto" : "Str_Backup_Mods_Created", path),
                    _loc.GetString("Str_T_Mods"));
                return true;
            }
            catch (Exception ex)
            {
                string message = _loc.Format("Str_Backup_Mods_Failed", ex.Message);
                bool proceed = await QMessageBoxWindow.ShowAsync(
                    message,
                    _loc.GetString("Str_Backup_Mods_Title"),
                    QMessageBoxButton.YesNo,
                    QMessageBoxImage.Warning) == QMessageBoxResult.Yes;

                if (!proceed)
                {
                    _toastService.ShowError(message, _loc.GetString("Str_T_Error"));
                }
                return proceed;
            }
        }

        private async Task ExecuteCreateModsBackupAsync()
        {
            if (!await TryBackupBeforeReplaceAsync(announceAuto: false).ConfigureAwait(true))
            {
                return;
            }
        }

        /// <summary>Установка одного кандидата и обновление его бейджа в списке.</summary>
        private async Task<bool> ApplyUpdateAsync(ModUpdateCandidate candidate)
        {
            string? newPath = await ModUpdateService.ApplyAsync(candidate).ConfigureAwait(true);
            if (newPath == null)
            {
                _toastService.ShowError(
                    _loc.Format("Str_Mods_UpdateFailed", candidate.OldFileName),
                    _loc.GetString("Str_T_Error"));
                return false;
            }

            _toastService.ShowSuccess(
                _loc.Format("Str_Mods_UpdatedOne", candidate.OldFileName, candidate.FreshName),
                _loc.GetString("Str_T_Updates"));

            _pendingUpdates.Remove(candidate);
            return true;
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
                _toastService.ShowError(_loc.Format("Str_Mod_ToggleError", ex.Message), _loc.GetString("Str_T_Error"));
            }
        }

        private async void ExecuteDeleteMod(LocalModItem? mod)
        {
            if (mod == null || !File.Exists(mod.FullPath)) return;

            if (await QMessageBoxWindow.ShowAsync(_loc.Format("Str_Mod_DeleteQuestion", mod.FileName), _loc.GetString("Str_T_DeleteModConfirm"), QMessageBoxButton.YesNo, QMessageBoxImage.Question) == QMessageBoxResult.Yes)
            {
                try
                {
                    File.Delete(mod.FullPath);
                    LocalMods.Remove(mod);
                    _toastService.ShowInfo(_loc.Format("Str_Mod_Deleted", mod.FileName), _loc.GetString("Str_T_Mods"));
                }
                catch (Exception ex)
                {
                    _toastService.ShowError(_loc.Format("Str_Mod_DeleteError", ex.Message), _loc.GetString("Str_T_Error"));
                }
            }
        }

        private static void OpenFolderCrossPlatform(string path)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                    Process.Start("explorer.exe", path);
                else if (OperatingSystem.IsMacOS())
                    Process.Start("open", path);
                else
                    Process.Start("xdg-open", path);
            }
            catch { }
        }

        private void ExecuteOpenModsFolder()
        {
            string modsDir = Path.Combine(_profile.FolderPath, "mods");
            Directory.CreateDirectory(modsDir);
            OpenFolderCrossPlatform(modsDir);
        }

        private void ExecuteOpenWorldsFolder()
        {
            string savesDir = Path.Combine(_profile.FolderPath, "saves");
            Directory.CreateDirectory(savesDir);
            OpenFolderCrossPlatform(savesDir);
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
                _toastService.ShowInfo(_loc.Format("Str_Backup_Creating", world.WorldName), _loc.GetString("Str_T_Backup"));
                await WorldBackupService.Instance.CreateBackupAsync(world.FullPath, _profile.FolderPath);
                LoadBackups();
                _toastService.ShowSuccess(_loc.Format("Str_Backup_Created", world.WorldName), _loc.GetString("Str_T_Backup"));
            }
            catch (Exception ex)
            {
                _toastService.ShowError(_loc.Format("Str_Backup_Error", ex.Message), _loc.GetString("Str_T_Error"));
            }
        }

        private async Task ExecuteRestoreBackupAsync(WorldBackupItem? backup)
        {
            if (backup == null || !File.Exists(backup.FullPath)) return;

            var result = await QMessageBoxWindow.ShowAsync(
                _loc.Format("Str_Dialog_RestoreWorld", backup.WorldName, backup.FileName),
                _loc.GetString("Str_Dialog_RestoreWorldTitle"),
                QMessageBoxButton.YesNo,
                QMessageBoxImage.Question);

            if (result != QMessageBoxResult.Yes) return;

            try
            {
                _toastService.ShowInfo(_loc.Format("Str_Restore_Running", backup.WorldName), _loc.GetString("Str_T_RestoreBackup"));
                await WorldBackupService.Instance.RestoreBackupAsync(backup.FullPath, _profile.FolderPath);
                LoadWorlds();
                LoadBackups();
                _toastService.ShowSuccess(_loc.Format("Str_Restore_Done", backup.WorldName), _loc.GetString("Str_T_RestoreBackup"));
            }
            catch (Exception ex)
            {
                _toastService.ShowError(_loc.Format("Str_Restore_Error", ex.Message), _loc.GetString("Str_T_Error"));
            }
        }

        private async void ExecuteDeleteBackup(WorldBackupItem? backup)
        {
            if (backup == null || !File.Exists(backup.FullPath)) return;

            var result = await QMessageBoxWindow.ShowAsync(
                _loc.Format("Str_Dialog_DeleteBackup", backup.FileName),
                _loc.GetString("Str_Dialog_DeleteBackupTitle"),
                QMessageBoxButton.YesNo,
                QMessageBoxImage.Warning);

            if (result != QMessageBoxResult.Yes) return;

            try
            {
                if (WorldBackupService.Instance.DeleteBackup(backup.FullPath))
                {
                    Backups.Remove(backup);
                    _toastService.ShowInfo(_loc.Format("Str_Backup_Deleted", backup.FileName), _loc.GetString("Str_T_Backup"));
                }
            }
            catch (Exception ex)
            {
                _toastService.ShowError(_loc.Format("Str_Backup_DeleteError", ex.Message), _loc.GetString("Str_T_Error"));
            }
        }

        private async void ExecuteDeleteWorld(WorldItem? world)
        {
            if (world == null || !Directory.Exists(world.FullPath)) return;

            if (await QMessageBoxWindow.ShowAsync(_loc.Format("Str_World_DeleteQuestion", world.WorldName), _loc.GetString("Str_T_DeleteConfirm"), QMessageBoxButton.YesNo, QMessageBoxImage.Warning) == QMessageBoxResult.Yes)
            {
                try
                {
                    Directory.Delete(world.FullPath, true);
                    Worlds.Remove(world);
                    _toastService.ShowInfo(_loc.Format("Str_World_Deleted", world.WorldName), _loc.GetString("Str_T_Worlds"));
                }
                catch (Exception ex)
                {
                    _toastService.ShowError(_loc.Format("Str_World_DeleteError", ex.Message), _loc.GetString("Str_T_Error"));
                }
            }
        }

        private async void ExecuteExportZip()
        {
            var filePath = await UiHelper.SaveFileAsync(
                _loc.GetString("Str_Dialog_ExportPack"),
                $"{_profile.Name}.zip",
                "zip",
                new FilePickerFileType("Zip Archive") { Patterns = new[] { "*.zip" } });

            if (!string.IsNullOrEmpty(filePath))
            {
                try
                {
                    if (File.Exists(filePath)) File.Delete(filePath);
                    ZipFile.CreateFromDirectory(_profile.FolderPath, filePath);
                    _toastService.ShowSuccess(_loc.GetString("Str_Export_Done"), _loc.GetString("Str_T_Export"));
                }
                catch (Exception ex)
                {
                    _toastService.ShowError(_loc.Format("Str_Export_Error", ex.Message), _loc.GetString("Str_T_Error"));
                }
            }
        }
    }
}
