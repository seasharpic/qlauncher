using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using MinecraftLauncher.Helpers;
using MinecraftLauncher.Models;

namespace MinecraftLauncher.Services
{
    public interface ISettingsService
    {
        LauncherSettings Settings { get; }
        LauncherSettings Load();
        void Save();
        void Save(LauncherSettings settings);

        /// <summary>
        /// Отложенное сохранение: несколько частых вызовов подряд приводят к одной
        /// записи файла. Нужно для свойств, которые сохраняются на каждое изменение.
        /// </summary>
        void SaveDebounced(TimeSpan? delay = null);
    }

    /// <summary>
    /// Хранение настроек в settings.json.
    ///
    /// Два изменения относительно прежней версии:
    ///  1. Save больше не проглатывает исключения. Пустой catch означал, что
    ///     read-only папка, забитый диск или занятый файл приводили к тихой
    ///     потере аккаунтов, токенов и настроек сборок — при перезапуске всё
    ///     просто выглядело как «ничего не сохранилось».
    ///  2. Добавлена синхронизация. Раньше Load() и Save() вызывались и с UI-потока,
    ///     и с фонового (учёт игрового времени), причём Load подменял _currentSettings
    ///     новым объектом, пока UI держал ссылку на старый. Теперь изменения
    ///     применяются к живому экземпляру, а ссылка на объект стабильна.
    /// </summary>
    public class SettingsService : ISettingsService
    {
        private static string SettingsFile => MinecraftLauncher.Helpers.LauncherPathHelper.GetSettingsFilePath();

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        // Lock на весь экземпляр: и запись в файл, и мутация полей настроек
        // должны быть атомарны относительно чтения из других потоков.
        private readonly object _gate = new object();

        private readonly LauncherSettings _currentSettings;

        public static SettingsService Instance { get; } = new SettingsService();

        /// <summary>Ссылка стабильна на всё время работы приложения.</summary>
        public LauncherSettings Settings => _currentSettings;

        public SettingsService()
        {
            _currentSettings = new LauncherSettings();
            LoadInto(_currentSettings);
        }

        /// <summary>
        /// Перечитывает файл в тот же экземпляр, сохраняя ссылку стабильной.
        /// Именно это устраняло гонку с фоновым потоком учёта игрового времени.
        /// </summary>
        public LauncherSettings Load()
        {
            lock (_gate)
            {
                LoadInto(_currentSettings);

                // Лимит скорости живёт в статике, потому что к нему обращаются
                // низкоуровневые загрузчики, у которых нет доступа к настройкам.
                BandwidthLimiter.Configure(_currentSettings.BandwidthLimitMbps);

                return _currentSettings;
            }
        }

        private void LoadInto(LauncherSettings target)
        {
            if (!File.Exists(SettingsFile))
            {
                CopyDefaults(target, new LauncherSettings());
                return;
            }

            try
            {
                string json = File.ReadAllText(SettingsFile);
                var loaded = JsonSerializer.Deserialize<LauncherSettings>(json) ?? new LauncherSettings();
                CopyDefaults(target, loaded);
            }
            catch (JsonException ex)
            {
                // Повреждённый JSON: отводим файл в сторону, чтобы не потерять его
                // при следующем Save, и стартуем с настройками по умолчанию.
                try
                {
                    string backup = SettingsFile + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    File.Move(SettingsFile, backup, overwrite: true);
                    CrashLogWriter.Write($"Settings file was corrupt and has been moved to '{backup}'. {ex.Message}");
                }
                catch (Exception backupEx)
                {
                    CrashLogWriter.Write($"Failed to preserve corrupt settings file. {backupEx.Message}");
                }

                CopyDefaults(target, new LauncherSettings());
            }
            catch (IOException ex)
            {
                // Файл занят другим процессом — продолжаем с текущим состоянием в памяти.
                CrashLogWriter.Write($"Failed to read settings file. {ex.Message}");
            }
        }

        public void Save()
        {
            Save(_currentSettings);
        }

        private CancellationTokenSource? _pendingSaveCts;
        private readonly object _debounceGate = new();

        /// <summary>
        /// Отложенное сохранение.
        ///
        /// Раньше смена выбранной версии писала settings.json на каждое изменение
        /// выбора, а переключение тем и настроек — из каждого сеттера. Это полная
        /// сериализация и запись файла на UI-потоке на каждое действие пользователя.
        /// Здесь вызовы накапливаются, и запись происходит один раз после паузы.
        /// </summary>
        public void SaveDebounced(TimeSpan? delay = null)
        {
            CancellationTokenSource cts;

            lock (_debounceGate)
            {
                _pendingSaveCts?.Cancel();
                _pendingSaveCts?.Dispose();

                cts = new CancellationTokenSource();
                _pendingSaveCts = cts;
            }

            var token = cts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(delay ?? TimeSpan.FromMilliseconds(400), token);
                }
                catch (OperationCanceledException)
                {
                    // Последующий вызов уже запланировал запись.
                    return;
                }

                if (token.IsCancellationRequested) return;

                try
                {
                    Save();
                }
                catch (Exception ex)
                {
                    CrashLogWriter.Write("SettingsService", "Debounced save failed", ex);
                }
            });
        }

        public void Save(LauncherSettings settings)
        {
            string json;

            lock (_gate)
            {
                if (settings != null && !ReferenceEquals(settings, _currentSettings))
                {
                    // Раньше здесь выполнялось присваивание _currentSettings = settings,
                    // из-за чего ссылки на старый объект у UI оставались невалидными.
                    CopyDefaults(_currentSettings, settings);
                }

                try
                {
                    json = JsonSerializer.Serialize(_currentSettings, JsonOptions);
                }
                catch (Exception ex)
                {
                    CrashLogWriter.Write($"Failed to serialize settings. {ex.Message}");
                    throw new IOException(LocalizationService.Instance.GetString("Str_Settings_SerializeFailed"), ex);
                }
            }

            // Файловую запись делаем под lock, но через временный файл + Move:
            // это исключает частично записанный settings.json при сбое питания.
            lock (_gate)
            {
                try
                {
                    string? dir = Path.GetDirectoryName(SettingsFile);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    string temp = SettingsFile + ".tmp";
                    File.WriteAllText(temp, json);

                    if (File.Exists(SettingsFile))
                    {
                        File.Move(temp, SettingsFile, overwrite: true);
                    }
                    else
                    {
                        File.Move(temp, SettingsFile);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Больше не молчим: вызывающий код может показать ошибку пользователю,
                    // а подробности уходят в crash-лог.
                    CrashLogWriter.Write($"Failed to save settings to '{SettingsFile}'. {ex.Message}");
                    throw new IOException(LocalizationService.Instance.Format("Str_Settings_SaveFailed", ex.Message), ex);
                }
            }
        }

        /// <summary>
        /// Переносит значения из source в target, заполняя отсутствующие
        /// поля значениями по умолчанию. Объекты-коллекции заменяем целиком,
        /// чтобы не смешивать состояние.
        /// </summary>
        private static void CopyDefaults(LauncherSettings target, LauncherSettings source)
        {
            target.GamePath = source.GamePath;
            target.RamMb = source.RamMb;
            target.JavaPath = source.JavaPath;
            target.CloseOnLaunch = source.CloseOnLaunch;
            target.EnableDiscordRpc = source.EnableDiscordRpc;
            target.EnableUiSounds = source.EnableUiSounds;
            target.HideServerIp = source.HideServerIp;
            target.AutoAddServers = source.AutoAddServers;
            target.ActiveAccount = source.ActiveAccount;
            target.LastSelectedVersion = source.LastSelectedVersion;
            // Значение приводится к каноническому имени здесь, а не в UI: старые
            // "default"/"aikar"/"zgc_shenandoaw" остались в settings.json у всех,
            // кто запускал прошлые версии, и должны один раз переписаться.
            target.JvmPreset = JvmOptimizationHelper.NormalizePresetName(source.JvmPreset);
            target.CustomJvmArgs = source.CustomJvmArgs;
            target.UseOptimizedJvmArgs = source.UseOptimizedJvmArgs;
            target.MirrorPreference = MirrorService.ParsePreference(source.MirrorPreference).ToString();
            target.ScreenWidth = source.ScreenWidth;
            target.ScreenHeight = source.ScreenHeight;
            target.IsFullScreen = source.IsFullScreen;
            target.GpuPreference = source.GpuPreference;
            target.CustomWallpaperPath = source.CustomWallpaperPath;
            target.IsDarkTheme = source.IsDarkTheme;
            target.AccentPreset = source.AccentPreset;
            target.Language = source.Language;
            target.CheckUpdatesOnStartup = source.CheckUpdatesOnStartup;
            target.ShowSplashOnStartup = source.ShowSplashOnStartup;
            target.UpdateChannel = source.UpdateChannel;
            target.UpdateMirror = source.UpdateMirror;
            target.BandwidthLimitMbps = source.BandwidthLimitMbps;
            target.SkippedVersion = source.SkippedVersion;
            target.AutoInjectServerIp = source.AutoInjectServerIp;
            target.AutoInjectServerName = source.AutoInjectServerName;
            target.Accounts = source.Accounts ?? new List<AccountProfile>();
            target.Modpacks = source.Modpacks ?? new List<ModpackProfile>();
        }
    }
}