using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using MinecraftLauncher.Helpers;

namespace MinecraftLauncher.Services.LaunchEngine
{
    /// <summary>
    /// Установка Fabric и Quilt: скачивание профиля версии, который потом
    /// подхватывает лаунчер.
    ///
    /// Исключения здесь — типизированные. Раньше здесь было шесть
    /// throw new Exception со строковым сообщением, и вызывающий код, который
    /// показывает пользователю только ex.Message, не мог отличить сетевую ошибку
    /// от битого JSON.
    /// </summary>
    public class ModLoaderService
    {
        private static readonly HttpClient HttpClient = new(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        });

        public async Task<string> InstallFabricAsync(string gameVersion, string gameRootPath)
        {
            string primaryMetaUrl = $"https://meta.fabricmc.net/v2/versions/loader/{gameVersion}";
            string mirrorMetaUrl = $"https://bmclapi2.bangbang93.com/fabric-meta/v2/versions/loader/{gameVersion}";

            string? metaJson = await FetchStringWithFallbackAsync(
                MirrorService.Instance.BuildCandidates(primaryMetaUrl, mirrorMetaUrl));
            if (string.IsNullOrEmpty(metaJson))
            {
                throw new IOException(LocalizationService.Instance.Format("Str_ModLoader_FabricMetaFailed", gameVersion));
            }

            string loaderVersion = ReadLoaderVersion(metaJson, "Fabric", gameVersion);

            string primaryProfileUrl = $"https://meta.fabricmc.net/v2/versions/loader/{gameVersion}/{loaderVersion}/profile/json";
            string mirrorProfileUrl = $"https://bmclapi2.bangbang93.com/fabric-meta/v2/versions/loader/{gameVersion}/{loaderVersion}/profile/json";

            string? profileJson = await FetchStringWithFallbackAsync(
                MirrorService.Instance.BuildCandidates(primaryProfileUrl, mirrorProfileUrl));
            if (string.IsNullOrEmpty(profileJson))
            {
                throw new IOException(LocalizationService.Instance.Format("Str_ModLoader_ProfileFailed", "Fabric"));
            }

            return await SaveProfileAsync(profileJson, gameRootPath, $"fabric-loader-{loaderVersion}-{gameVersion}", "Fabric", loaderVersion, gameVersion);
        }

        public async Task<string> InstallQuiltAsync(string gameVersion, string gameRootPath)
        {
            string primaryMetaUrl = $"https://meta.quiltmc.net/v3/versions/loader/{gameVersion}";
            // У Quilt зеркала нет: BuildCandidates с null-зеркалом вернёт один кандидат.
            string? metaJson = await FetchStringWithFallbackAsync(
                MirrorService.Instance.BuildCandidates(primaryMetaUrl, null));
            if (string.IsNullOrEmpty(metaJson))
            {
                throw new IOException(LocalizationService.Instance.Format("Str_ModLoader_FabricMetaFailed", gameVersion));
            }

            string loaderVersion = ReadLoaderVersion(metaJson, "Quilt", gameVersion);

            string profileUrl = $"https://meta.quiltmc.net/v3/versions/loader/{gameVersion}/{loaderVersion}/profile/json";
            string? profileJson = await FetchStringWithFallbackAsync(
                MirrorService.Instance.BuildCandidates(profileUrl, null));
            if (string.IsNullOrEmpty(profileJson))
            {
                throw new IOException(LocalizationService.Instance.Format("Str_ModLoader_ProfileFailed", "Quilt"));
            }

            return await SaveProfileAsync(profileJson, gameRootPath, $"quilt-loader-{loaderVersion}-{gameVersion}", "Quilt", loaderVersion, gameVersion);
        }

        /// <summary>
        /// Извлекает версию лоадера из ответа Meta API.
        /// </summary>
        private static string ReadLoaderVersion(string metaJson, string loaderName, string gameVersion)
        {
            using var doc = JsonDocument.Parse(metaJson);

            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
            {
                throw new InvalidOperationException(LocalizationService.Instance.Format("Str_ModLoader_NotFound", loaderName, gameVersion));
            }

            if (!doc.RootElement[0].TryGetProperty("loader", out var loaderElement)
                || !loaderElement.TryGetProperty("version", out var versionElement))
            {
                throw new InvalidDataException(LocalizationService.Instance.Format("Str_ModLoader_BadApi", loaderName, gameVersion));
            }

            string loaderVersion = versionElement.GetString() ?? "";
            if (string.IsNullOrWhiteSpace(loaderVersion))
            {
                throw new InvalidDataException(LocalizationService.Instance.Format("Str_ModLoader_EmptyVersion", loaderName));
            }

            return loaderVersion;
        }

        /// <summary>
        /// Сохраняет профиль версии на диск и возвращает его идентификатор.
        /// Профиль пишется в атомарном виде через временный файл, чтобы прерванная
        /// загрузка не оставляла обрезанный JSON, который сломает запуск игры.
        /// </summary>
        private static async Task<string> SaveProfileAsync(
            string profileJson,
            string gameRootPath,
            string profileId,
            string loaderName,
            string loaderVersion,
            string gameVersion)
        {
            // Профиль влияет на classpath и на mainClass, поэтому проверяем,
            // что он хотя бы разбирается как JSON, до записи на диск.
            try
            {
                using var probe = JsonDocument.Parse(profileJson);
                if (probe.RootElement.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException();
                }
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException(
                    LocalizationService.Instance.Format("Str_ModLoader_Corrupt", loaderName, loaderVersion, gameVersion, ex.Message), ex);
            }

            string versionDir = Path.Combine(gameRootPath, "versions", profileId);
            Directory.CreateDirectory(versionDir);

            string versionJsonPath = Path.Combine(versionDir, $"{profileId}.json");
            string tempPath = versionJsonPath + ".tmp";

            await File.WriteAllTextAsync(tempPath, profileJson);

            if (File.Exists(versionJsonPath))
            {
                File.Move(tempPath, versionJsonPath, overwrite: true);
            }
            else
            {
                File.Move(tempPath, versionJsonPath);
            }

            return profileId;
        }

        /// <summary>
        /// Возвращает ответ первого доступного кандидата.
        /// Порядок кандидатов задан вызывающим кодом через MirrorService.
        /// </summary>
        private static async Task<string?> FetchStringWithFallbackAsync(IReadOnlyList<string> candidates)
        {
            // 10 секунд на весь запрос (подключение + чтение тела) обрывал загрузку
            // на медленной сети, поэтому таймаут увеличен.
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(60));

            foreach (var url in candidates)
            {
                try
                {
                    return await HttpClient.GetStringAsync(url, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
                catch (HttpRequestException)
                {
                    // Пробуем следующий источник.
                }
            }

            return null;
        }
    }
}