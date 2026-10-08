using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MinecraftLauncher.Helpers;
using MinecraftLauncher.Services.LaunchEngine.Models;

namespace MinecraftLauncher.Services.LaunchEngine
{
    public class AssetService
    {
        private static readonly HttpClient HttpClient = new(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            MaxConnectionsPerServer = 32
        });

        private const string PrimaryResourceHost = "https://resources.download.minecraft.net";
        private const string MirrorResourceHost = "https://bmclapi2.bangbang93.com/assets";

        public async Task EnsureAssetsAsync(string gameRootPath, MojangVersionInfo versionInfo, IProgress<LaunchProgress>? progress = null)
        {
            if (versionInfo.AssetIndex == null || string.IsNullOrEmpty(versionInfo.AssetIndex.Url))
                return;

            string assetsRoot = Path.Combine(gameRootPath, "assets");
            string indexesDir = Path.Combine(assetsRoot, "indexes");
            string objectsDir = Path.Combine(assetsRoot, "objects");
            Directory.CreateDirectory(indexesDir);
            Directory.CreateDirectory(objectsDir);

            string indexFilePath = Path.Combine(indexesDir, $"{versionInfo.AssetIndex.Id}.json");

            if (!File.Exists(indexFilePath))
            {
                progress?.Report(new LaunchProgress
                {
                    Phase = LaunchPhase.DownloadingAssets,
                    StatusText = LocalizationService.Instance.GetString("Str_Launch_DownloadIndex"),
                    Percentage = 62
                });

                string mirrorIndexUrl = MirrorService.BuildMojangMirrorUrl(versionInfo.AssetIndex.Url);
                // Порядок источников задаёт MirrorService: официальный Mojang
                // первым, зеркало запасным (или наоборот, если официальный хост
                // не отвечает).
                string? indexJson = await FetchStringWithFallbackAsync(
                    MirrorService.Instance.BuildCandidates(versionInfo.AssetIndex.Url, mirrorIndexUrl));
                if (string.IsNullOrEmpty(indexJson)) return;

                // Индекс задаёт ожидаемый хеш каждого ассета, поэтому сверяем и его
                // самого: иначе зеркало может подменить список объектов.
                if (!string.IsNullOrEmpty(versionInfo.AssetIndex.Sha1) && !HashHelper.VerifySha1(System.Text.Encoding.UTF8.GetBytes(indexJson), versionInfo.AssetIndex.Sha1))
                {
                    return;
                }

                await File.WriteAllTextAsync(indexFilePath, indexJson);
            }

            string rawIndex = await File.ReadAllTextAsync(indexFilePath);
            using var doc = JsonDocument.Parse(rawIndex);
            if (!doc.RootElement.TryGetProperty("objects", out var objectsElement)) return;

            var missingObjects = new List<(string hash, long size, string localPath)>();

            foreach (var prop in objectsElement.EnumerateObject())
            {
                if (prop.Value.TryGetProperty("hash", out var hashEl))
                {
                    string hash = hashEl.GetString() ?? "";
                    if (hash.Length >= 2)
                    {
                        string subFolder = hash.Substring(0, 2);
                        string localPath = Path.Combine(objectsDir, subFolder, hash);
                        long size = prop.Value.TryGetProperty("size", out var s) ? s.GetInt64() : 0;

                        if (!File.Exists(localPath) || (size > 0 && new FileInfo(localPath).Length != size))
                        {
                            missingObjects.Add((hash, size, localPath));
                        }
                    }
                }
            }

            int total = missingObjects.Count;
            if (total > 0)
            {
                int completed = 0;
                using var semaphore = new SemaphoreSlim(24);
                var tasks = new List<Task>();

                foreach (var obj in missingObjects)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        await semaphore.WaitAsync();
                        try
                        {
                            string sub = obj.hash.Substring(0, 2);
                            string primaryUrl = $"{PrimaryResourceHost}/{sub}/{obj.hash}";
                            string mirrorUrl = $"{MirrorResourceHost}/{sub}/{obj.hash}";

                            await DownloadFileWithFallbackAsync(
                                MirrorService.Instance.BuildCandidates(primaryUrl, mirrorUrl),
                                obj.localPath,
                                obj.hash);

                            int cur = Interlocked.Increment(ref completed);
                            int pct = 65 + (int)((cur / (double)total) * 25);
                            progress?.Report(new LaunchProgress
                            {
                                Phase = LaunchPhase.DownloadingAssets,
                                StatusText = LocalizationService.Instance.Format("Str_Launch_DownloadAssets", cur, total),
                                Percentage = pct
                            });
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }));
                }

                await Task.WhenAll(tasks);
            }
        }

        private static async Task<string?> FetchStringWithFallbackAsync(IReadOnlyList<string> candidates)
        {
            foreach (string url in candidates)
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    return await HttpClient.GetStringAsync(url, cts.Token);
                }
                catch
                {
                    // Этот кандидат не ответил — пробуем следующий.
                }
            }

            return null;
        }

        /// <summary>
        /// expectedHash — SHA-1 ассета. В манифесте Mojang это хеш одновременно
        /// является именем файла, поэтому сверка почти бесплатна, а подмена файла
        /// на зеркале больше не проходит незамеченной.
        /// </summary>
        private static async Task DownloadFileWithFallbackAsync(IReadOnlyList<string> candidates, string destinationPath, string? expectedHash = null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

            byte[]? data = await SecureHttp.TryDownloadBytesAsync(
                candidates,
                TimeSpan.FromMinutes(1));

            if (data == null || data.Length == 0) return;

            if (!string.IsNullOrEmpty(expectedHash) && !HashHelper.VerifySha1(data, expectedHash))
            {
                return;
            }

            await File.WriteAllBytesAsync(destinationPath, data);
        }
    }
}
