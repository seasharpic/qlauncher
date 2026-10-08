using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using MinecraftLauncher.Helpers;
using MinecraftLauncher.Models;
using MinecraftLauncher.Services.LaunchEngine;
using MinecraftLauncher.Services.LaunchEngine.Models;

namespace MinecraftLauncher.Services
{
    public interface IMinecraftLaunchService
    {
        event Action<int, int>? FileProgressChanged;
        event Action<long, long, double, string>? ByteProgressChanged;

        Task InitializeAsync(string gamePath);
        Task<List<string>> GetAllReleaseVersionsAsync();
        Task<Process> LaunchGameAsync(
            string launchVersion,
            AccountProfile account,
            LauncherSettings settings,
            string? autoJoinServerIp,
            Action<string>? statusCallback);
        Task<string> InstallFabricAsync(string gameVersion, string gamePath);
        Task<string> InstallQuiltAsync(string gameVersion, string gamePath);
        Task<string> InstallForgeAsync(string gameVersion);
        Task<string> InstallNeoForgeAsync(string gameVersion);
        Task DownloadModrinthModAsync(string slug, string gameVersion, string modsPath);
    }

    public class MinecraftLaunchService : IMinecraftLaunchService
    {
        private readonly IMinecraftLaunchEngine _engine;
        private readonly IAntiBlockService _antiBlockService;
        private readonly IJavaService _javaService;
        private readonly ISettingsService _settingsService;

        private string _gamePath = "";

        /// <summary>
        /// Продлевает сессию Microsoft перед запуском игры.
        ///
        /// Для офлайн-аккаунтов делать нечего: метки токена у них нет.
        /// Если refresh не удался, запуск продолжается со старым токеном —
        /// возможно, он ещё жив, а возможно, сервер сообщит об отказе.
        /// Бросать исключение здесь означало бы запретить запуск из-за
        /// сети, даже когда всё остальное готово.
        /// </summary>
        private static async Task RefreshAccountSessionAsync(
            AccountProfile account,
            Action<string>? statusCallback)
        {
            if (string.IsNullOrEmpty(account.AccessToken) ||
                string.Equals(account.AccessToken, LaunchOptions.OfflineAccessToken, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                statusCallback?.Invoke(LocalizationService.Instance.GetString("Str_Auth_Refreshing"));

                var refresh = await MicrosoftAuthService.Instance.TryRefreshAsync(account.Nickname);

                if (refresh == null || !refresh.IsUsable) return;

                account.AccessToken = refresh.AccessToken;

                if (!string.IsNullOrEmpty(refresh.Uuid))
                {
                    account.Uuid = refresh.Uuid;
                }

                // Токен меняется на каждом запуске, поэтому настройки надо
                // перезаписать: иначе после перезапуска лаунчера вернётся
                // истёкший.
                try
                {
                    SettingsService.Instance.Save();
                }
                catch { }
            }
            catch (Exception ex)
            {
                CrashLogWriter.Write("MinecraftLaunchService", "Microsoft session refresh skipped", ex);
            }
        }

        // Локализация для строки скорости загрузки.
        private static readonly ILocalizationService _loc = LocalizationService.Instance;

        /// <summary>
        /// Подпись скорости для строки прогресса. Единицы выводятся в
        /// локализованном виде, иначе «MB/s» оставалось бы русским интерфейсом
        /// чужеродным.
        /// </summary>
        private static string FormatSpeed(double bytesPerSecond)
        {
            if (bytesPerSecond <= 1) return "";

            return bytesPerSecond >= 1024 * 1024
                ? _loc.Format("Str_Speed_MbPerSec", $"{(bytesPerSecond / (1024.0 * 1024.0)):F1}")
                : _loc.Format("Str_Speed_KbPerSec", $"{(bytesPerSecond / 1024.0):F0}");
        }

        public event Action<int, int>? FileProgressChanged;
        public event Action<long, long, double, string>? ByteProgressChanged;

        public static MinecraftLaunchService Instance { get; } = new MinecraftLaunchService(
            MinecraftLaunchEngine.Instance,
            AntiBlockService.Instance,
            JavaService.Instance,
            SettingsService.Instance);

        public MinecraftLaunchService(
            IMinecraftLaunchEngine engine,
            IAntiBlockService antiBlockService,
            IJavaService javaService,
            ISettingsService settingsService)
        {
            _engine = engine;
            _antiBlockService = antiBlockService;
            _javaService = javaService;
            _settingsService = settingsService;
        }

        /// <summary>
        /// Инициализация папки игры.
        ///
        /// FixAllFabricVersions обходит все установленные версии, читая и
        /// перезаписывая их JSON. Метод синхронный и вызывался из Initialize()
        /// при каждом старте, то есть на UI-потоке — на большой папке это заметная
        /// задержка. Теперь обход выполняется в Task.Run.
        /// </summary>
        public async Task InitializeAsync(string gamePath)
        {
            _gamePath = gamePath;

            await Task.Run(() =>
            {
                Directory.CreateDirectory(gamePath);
                _antiBlockService.FixAllFabricVersions(gamePath);
            });
        }

        

        public async Task<List<string>> GetAllReleaseVersionsAsync()
        {
            var result = new List<string>();

            string versionsDir = Path.Combine(_gamePath, "versions");
            if (Directory.Exists(versionsDir))
            {
                foreach (var dir in Directory.GetDirectories(versionsDir))
                {
                    string verName = Path.GetFileName(dir);
                    string jsonPath = Path.Combine(dir, $"{verName}.json");
                    if (File.Exists(jsonPath))
                    {
                        result.Add(verName);
                    }
                }
            }

            try
            {
                var manifest = await _engine.GetManifestAsync();
                if (manifest != null)
                {
                    foreach (var v in manifest.Versions)
                    {
                        if (v.Type == "release" && !result.Contains(v.Id))
                        {
                            result.Add(v.Id);
                        }
                    }
                }
            }
            catch { }

            return result;
        }

        public async Task<Process> LaunchGameAsync(
            string launchVersion,
            AccountProfile account,
            LauncherSettings settings,
            string? autoJoinServerIp,
            Action<string>? statusCallback)
        {
            await InitializeAsync(settings.GamePath);

            string cleanVersion = launchVersion.Replace("⭐", "").Trim();
            string candidatePackName = cleanVersion;
            if (candidatePackName.Contains(" ("))
            {
                candidatePackName = candidatePackName.Substring(0, candidatePackName.LastIndexOf(" (")).Trim();
            }

            var pack = settings.Modpacks.Find(p =>
                p.Name.Equals(candidatePackName, StringComparison.OrdinalIgnoreCase) ||
                $"{p.Name} ({p.Loader})".Equals(cleanVersion, StringComparison.OrdinalIgnoreCase) ||
                p.Name.Equals(cleanVersion, StringComparison.OrdinalIgnoreCase));

            string realVersionId = cleanVersion;
            string? instanceFolderPath = null;
            string targetGameVer = "1.20.1";

            if (pack != null)
            {
                instanceFolderPath = pack.FolderPath;
                targetGameVer = !string.IsNullOrWhiteSpace(pack.GameVersion)
                    ? pack.GameVersion
                    : "1.20.1";

                if (string.Equals(pack.Loader, "Fabric", StringComparison.OrdinalIgnoreCase))
                {
                    statusCallback?.Invoke(LocalizationService.Instance.GetString("Str_Install_Fabric"));
                    realVersionId = await _engine.InstallFabricAsync(targetGameVer, _gamePath);
                }
                else if (string.Equals(pack.Loader, "Quilt", StringComparison.OrdinalIgnoreCase))
                {
                    statusCallback?.Invoke(LocalizationService.Instance.GetString("Str_Install_Quilt"));
                    realVersionId = await _engine.InstallQuiltAsync(targetGameVer, _gamePath);
                }
                else if (string.Equals(pack.Loader, "Forge", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(pack.Loader, "NeoForge", StringComparison.OrdinalIgnoreCase))
                {
                    // Раньше эти лоадеры попадали в общую ветку else вместе с Vanilla
                    // и Custom, где realVersionId просто становился ванильной версией.
                    // Итог: сборка запускалась как чистый Minecraft, все моды
                    // игнорировались, и пользователь не получал никакого сообщения.
                    //
                    // Установщика Forge/NeoForge в проекте нет, поэтому честно
                    // сообщаем об этом вместо молчаливого запуска неправильной игры.
                    throw new NotSupportedException(
                        LocalizationService.Instance.Format("Str_Loader_Unsupported", pack.Loader, pack.Name));
                }
                else
                {
                    realVersionId = targetGameVer;
                }
            }
            else
            {
                targetGameVer = cleanVersion;
                realVersionId = cleanVersion;
            }

            statusCallback?.Invoke(LocalizationService.Instance.GetString("Str_Install_CheckJava"));

            // Версия Java по версии игры берётся из таблицы, а она для будущих
            // релизов (26.x и далее) отдаёт 21. Но моды могут требовать больше:
            // mixin-конфиг с compatibilityLevel "JAVA_25" роняет Fabric Loader
            // с "The requested compatibility level JAVA_25 could not be set" —
            // уже внутри игры, когда лаунчер ничего исправить не может.
            // Поэтому требование читается из самих модов и берётся максимум.
            string modsFolder = Path.Combine(instanceFolderPath ?? _gamePath, "mods");

            string javaPath = await _javaService.ResolveJavaExecutableAsync(
                settings.JavaPath, targetGameVer, statusCallback, modsFolder);

            // Токен Minecraft живёт около двух часов. Раньше здесь стоял
            // сохранённый в настройках access_token, и после его истечения
            // игра отказывалась пускать с ошибкой авторизации — приходилось
            // заново проходить вход через браузер. Теперь сессия продлевается
            // молча по refresh-токену.
            await RefreshAccountSessionAsync(account, statusCallback);

            string? serverIp = null;
            int? serverPort = null;
            if (!string.IsNullOrWhiteSpace(autoJoinServerIp))
            {
                serverIp = autoJoinServerIp;
                serverPort = 25565;
                if (serverIp.Contains(':'))
                {
                    var parts = serverIp.Split(':');
                    serverIp = parts[0];
                    if (int.TryParse(parts[1], out int p)) serverPort = p;
                }
            }

            // Раньше здесь стояло settings.GamePath, хотя сервис держит _gamePath,
            // инициализированный выше. Два значения могли разойтись (например,
            // при смене папки игры), и файлы писались в разные места.
            string effectiveGamePath = _gamePath;

            var launchOptions = new LaunchOptions
            {
                GameRootPath = effectiveGamePath,
                InstancePath = instanceFolderPath ?? effectiveGamePath,
                VersionId = realVersionId,
                JavaPath = javaPath,
                PlayerName = account.Nickname,
                Uuid = account.Uuid,
                AccessToken = account.AccessToken,
                RamMb = settings.RamMb,
                JvmPreset = settings.JvmPreset,
                CustomJvmArgs = settings.CustomJvmArgs,
                UseOptimizedJvmArgs = settings.UseOptimizedJvmArgs,
                ScreenWidth = settings.ScreenWidth,
                ScreenHeight = settings.ScreenHeight,
                IsFullScreen = settings.IsFullScreen,
                GpuPreference = settings.GpuPreference,
                ServerIp = serverIp,
                ServerPort = serverPort
            };

            // Скорость считается здесь: событие прогресса отдавало пустую строку,
            // поэтому строка «12.4 / 40.0 МБ (31%) • » теряла значение.
            var rate = new TransferRateTracker();

            var progress = new Progress<LaunchProgress>(p =>
            {
                if (!string.IsNullOrEmpty(p.StatusText))
                {
                    statusCallback?.Invoke(p.StatusText);
                }
                if (p.Percentage > 0)
                {
                    FileProgressChanged?.Invoke(p.Percentage, 100);
                    ByteProgressChanged?.Invoke(
                        p.CurrentBytes,
                        p.TotalBytes,
                        p.Percentage / 100.0,
                        FormatSpeed(rate.Feed(p.CurrentBytes)));
                }
            });

            return await _engine.LaunchAsync(launchOptions, progress);
        }

        public Task<string> InstallFabricAsync(string gameVersion, string gamePath) =>
            _engine.InstallFabricAsync(gameVersion, gamePath);

        public Task<string> InstallQuiltAsync(string gameVersion, string gamePath) =>
            _engine.InstallQuiltAsync(gameVersion, gamePath);

        /// <summary>
        /// Заглушки: установщиков Forge/NeoForge в проекте нет.
        ///
        /// Раньше эти методы молча возвращали версию игры, создавая впечатление,
        /// что установка прошла. Теперь они сообщают об отсутствии поддержки —
        /// вызов LaunchGameAsync для таких сборок отклоняется раньше, с понятным
        /// сообщением пользователю.
        /// </summary>
        public Task<string> InstallForgeAsync(string gameVersion) =>
            throw new NotSupportedException(
                LocalizationService.Instance.GetString("Str_Loader_ForgeUnsupported"));

        public Task<string> InstallNeoForgeAsync(string gameVersion) =>
            throw new NotSupportedException(
                LocalizationService.Instance.GetString("Str_Loader_NeoForgeUnsupported"));

        public async Task DownloadModrinthModAsync(string slug, string gameVersion, string modsPath)
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "QLauncher/2.0");

                string url = $"https://api.modrinth.com/v2/project/{slug}/version?game_versions=[\"{gameVersion}\"]&loaders=[\"fabric\"]";
                string json = await client.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.GetArrayLength() > 0)
                {
                    JsonElement targetVersion = root[0];
                    foreach (var verItem in root.EnumerateArray())
                    {
                        if (verItem.TryGetProperty("version_type", out var vt) && vt.GetString() == "release")
                        {
                            targetVersion = verItem;
                            break;
                        }
                    }

                    var files = targetVersion.GetProperty("files");
                    if (files.GetArrayLength() > 0)
                    {
                        string downloadUrl = files[0].GetProperty("url").GetString() ?? "";
                        string fileName = files[0].GetProperty("filename").GetString() ?? "";
                        string filePath = Path.Combine(modsPath, fileName);

                        Directory.CreateDirectory(modsPath);
                        byte[] fileBytes = await client.GetByteArrayAsync(downloadUrl);
                        await File.WriteAllBytesAsync(filePath, fileBytes);
                    }
                }
            }
            catch { }
        }
    }
}
