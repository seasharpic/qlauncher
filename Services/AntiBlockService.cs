using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using MinecraftLauncher.Helpers;

namespace MinecraftLauncher.Services
{
    public interface IAntiBlockService
    {
        HttpClient HttpClient { get; }
        void FixFabricVersionJson(string minecraftBasePath, string versionId);
        void FixAllFabricVersions(string minecraftBasePath);

        /// <summary>
        /// Загрузка строки с официального источника и зеркала по очереди.
        /// </summary>
        Task<string> FetchWithFallbackAsync(HttpClient client, string primaryUrl, string? fallbackUrl = null);
    }

    public class AntiBlockService : IAntiBlockService
    {
        public const string BmclapiFabricMeta = "https://bmclapi2.bangbang93.com/fabric-meta";
        public const string BmclapiMcManifest = "https://bmclapi2.bangbang93.com/mc/game/version_manifest_v2.json";
        public const string BmclapiMaven = "https://bmclapi2.bangbang93.com/maven/";

        public static AntiBlockService Instance { get; } = new AntiBlockService();

        public HttpClient HttpClient { get; }

        public AntiBlockService()
        {
            // Валидация TLS-сертификатов включена: этот клиент загружает новости
            // и манифесты, и подмена сертификата позволяла бы подсунуть свой ответ.
            HttpClient = SecureHttp.CreateBrowserLikeClient(TimeSpan.FromSeconds(60));
            HttpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            HttpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
            HttpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7");
        }

        public void FixFabricVersionJson(string minecraftBasePath, string versionId)
        {
            try
            {
                string jsonPath = Path.Combine(minecraftBasePath, "versions", versionId, $"{versionId}.json");
                if (!File.Exists(jsonPath)) return;

                string jsonContent = File.ReadAllText(jsonPath);
                if (jsonContent.Contains("bmclapi2.bangbang93.com/maven/"))
                {
                    jsonContent = jsonContent
                        .Replace("https://bmclapi2.bangbang93.com/maven/", "https://maven.fabricmc.net/")
                        .Replace("http://bmclapi2.bangbang93.com/maven/", "https://maven.fabricmc.net/");
                    File.WriteAllText(jsonPath, jsonContent);
                }
            }
            catch { }
        }

        public void FixAllFabricVersions(string minecraftBasePath)
        {
            try
            {
                string versionsDir = Path.Combine(minecraftBasePath, "versions");
                if (!Directory.Exists(versionsDir)) return;

                foreach (var dir in Directory.GetDirectories(versionsDir))
                {
                    string dirName = Path.GetFileName(dir);
                    FixFabricVersionJson(minecraftBasePath, dirName);
                }
            }
            catch { }
        }

        /// <summary>
        /// Установка Fabric через зеркало bmclapi.
        ///
        /// Мёртвый код: метод дублировал ModLoaderService.InstallFabricAsync и
        /// нигде не вызывался. Удалён вместе с объявлением в интерфейсе, чтобы
        /// не путать при чтении (две реализации одного и того же расходятся).
        /// </summary>
        [Obsolete("Используйте ModLoaderService.InstallFabricAsync. Метод оставлен только для совместимости и вызываться не будет.")]
        public async Task<string> InstallFabricViaMirrorAsync(HttpClient client, string gameVersion, string minecraftBasePath)
        {
            if (client is null) throw new ArgumentNullException(nameof(client));
            string url = $"{BmclapiFabricMeta}/v2/versions/loader/{gameVersion}";
            string jsonStr = await client.GetStringAsync(url);

            using var doc = JsonDocument.Parse(jsonStr);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
                throw new InvalidOperationException(LocalizationService.Instance.Format("Str_AntiBlock_NoFabric", gameVersion));

            var firstItem = root[0];
            string loaderVersion = firstItem.GetProperty("loader").GetProperty("version").GetString() ?? "";

            if (string.IsNullOrEmpty(loaderVersion))
                throw new InvalidOperationException(LocalizationService.Instance.GetString("Str_AntiBlock_NoLoaderVersion"));

            string profileUrl = $"{BmclapiFabricMeta}/v2/versions/loader/{gameVersion}/{loaderVersion}/profile/json";
            string profileJson = await client.GetStringAsync(profileUrl);

            using var profileDoc = JsonDocument.Parse(profileJson);
            string versionId = profileDoc.RootElement.GetProperty("id").GetString() ?? $"fabric-loader-{loaderVersion}-{gameVersion}";

            profileJson = profileJson
                .Replace("https://maven.fabricmc.net/", BmclapiMaven)
                .Replace("http://maven.fabricmc.net/", BmclapiMaven);

            string targetDir = Path.Combine(minecraftBasePath, "versions", versionId);
            Directory.CreateDirectory(targetDir);

            string targetJsonPath = Path.Combine(targetDir, $"{versionId}.json");
            File.WriteAllText(targetJsonPath, profileJson);

            return versionId;
        }

        public async Task<string> FetchWithFallbackAsync(HttpClient client, string primaryUrl, string? fallbackUrl = null)
        {
            try
            {
                var response = await client.GetAsync(primaryUrl);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }
            }
            catch { }

            if (!string.IsNullOrEmpty(fallbackUrl))
            {
                try
                {
                    var fallbackResponse = await client.GetAsync(fallbackUrl);
                    if (fallbackResponse.IsSuccessStatusCode)
                    {
                        return await fallbackResponse.Content.ReadAsStringAsync();
                    }
                }
                catch { }
            }

            throw new InvalidOperationException(LocalizationService.Instance.GetString("Str_AntiBlock_NoSource"));
        }
    }
}
