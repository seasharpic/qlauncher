using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using MinecraftLauncher.Helpers;
using MinecraftLauncher.Services.LaunchEngine.Models;

namespace MinecraftLauncher.Services.LaunchEngine
{
    public class MojangManifestService
    {
        // Локализация для статусов прогресса и сообщений об ошибках загрузки.
        private static readonly ILocalizationService L = LocalizationService.Instance;
        private static readonly HttpClient HttpClient = new(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            MaxConnectionsPerServer = 32
        });

        private const string PrimaryManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
        private const string MirrorManifestUrl = "https://bmclapi2.bangbang93.com/mc/game/version_manifest_v2.json";

        public async Task<MojangManifest?> GetManifestAsync()
        {
            string? json = await FetchFirstAsync(
                MirrorService.Instance.BuildCandidates(PrimaryManifestUrl, MirrorManifestUrl),
                TimeSpan.FromSeconds(8));

            if (string.IsNullOrEmpty(json)) return null;

            return JsonSerializer.Deserialize<MojangManifest>(json);
        }

        public async Task<MojangVersionInfo?> ResolveVersionInfoAsync(string gameRootPath, string versionId)
        {
            // Страховка от рекурсии без границ: inheritsFrom приходит из JSON на диске
            // или из сети, то есть повреждённый локальный файл или враждебное зеркало
            // могли дать циклическую цепочку. Раньше это приводило к
            // StackOverflowException, который необратим и не ловится обработчиком.
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return await ResolveVersionInfoCoreAsync(gameRootPath, versionId, visited);
        }

        private async Task<MojangVersionInfo?> ResolveVersionInfoCoreAsync(string gameRootPath, string versionId, HashSet<string> visited)
        {
            if (!visited.Add(versionId))
            {
                CrashLogWriter.Write(
                    "MojangManifestService",
                    $"Circular inheritsFrom chain detected at version '{versionId}'.",
                    null);

                return null;
            }

            // Ограничение на длину цепочки как вторая защита.
            const int MaxInheritanceDepth = 16;
            if (visited.Count > MaxInheritanceDepth)
            {
                return null;
            }

            string versionDir = Path.Combine(gameRootPath, "versions", versionId);
            string versionJsonPath = Path.Combine(versionDir, $"{versionId}.json");

            MojangVersionInfo? info = null;

            if (File.Exists(versionJsonPath))
            {
                try
                {
                    string localJson = await File.ReadAllTextAsync(versionJsonPath);
                    info = JsonSerializer.Deserialize<MojangVersionInfo>(localJson);
                }
                catch { }
            }

            if (info == null)
            {
                var manifest = await GetManifestAsync();
                var verHeader = manifest?.Versions.Find(v => v.Id.Equals(versionId, StringComparison.OrdinalIgnoreCase));
                if (verHeader != null && !string.IsNullOrEmpty(verHeader.Url))
                {
                    string mirrorUrl = MirrorService.BuildMojangMirrorUrl(verHeader.Url);
                    string? json = await FetchFirstAsync(
                        MirrorService.Instance.BuildCandidates(verHeader.Url, mirrorUrl),
                        TimeSpan.FromSeconds(8));
                    if (!string.IsNullOrEmpty(json))
                    {
                        Directory.CreateDirectory(versionDir);
                        await File.WriteAllTextAsync(versionJsonPath, json);
                        info = JsonSerializer.Deserialize<MojangVersionInfo>(json);
                    }
                }
            }

            if (info != null && !string.IsNullOrEmpty(info.InheritsFrom))
            {
                var parentInfo = await ResolveVersionInfoCoreAsync(gameRootPath, info.InheritsFrom, visited);
                if (parentInfo != null)
                {
                    MergeVersionInfo(info, parentInfo);
                }
            }

            return info;
        }

        public async Task EnsureClientJarAsync(string gameRootPath, MojangVersionInfo versionInfo, IProgress<LaunchProgress>? progress = null)
        {
            if (versionInfo.Downloads?.Client == null) return;

            string targetVersionId = !string.IsNullOrEmpty(versionInfo.InheritsFrom) ? versionInfo.InheritsFrom : versionInfo.Id;
            string clientJarPath = Path.Combine(gameRootPath, "versions", targetVersionId, $"{targetVersionId}.jar");
            var clientDownload = versionInfo.Downloads.Client;

            if (File.Exists(clientJarPath) && new FileInfo(clientJarPath).Length == clientDownload.Size)
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(clientJarPath)!);
            progress?.Report(new LaunchProgress
            {
                Phase = LaunchPhase.CheckingVersionMetadata,
                StatusText = L.Format("Str_Launch_DownloadingCore", targetVersionId),
                Percentage = 10
            });

            string mirrorUrl = MirrorService.BuildMojangMirrorUrl(clientDownload.Url);

            // Порядок источников: сначала официальный Mojang, зеркало — запасным.
            // Раньше зеркало баланга93 было основным, то есть ядро игры тянулось
            // с недоверенного стороннего хоста.
            byte[]? data = await DownloadFirstAsync(
                MirrorService.Instance.BuildCandidates(clientDownload.Url, mirrorUrl),
                TimeSpan.FromSeconds(15));

            if (data == null)
            {
                // Раньше здесь был просто return без else: при неудаче загрузки
                // запуск продолжался без client.jar и падал позже в игре
                // с ClassNotFoundException, без указания причины.
                throw new IOException(
                    L.Format("Str_Launch_ClientJarFailed", targetVersionId));
            }

            // client.jar — исполняемый артефакт, сверяем SHA-1 из манифеста.
            if (!string.IsNullOrEmpty(clientDownload.Sha1) && !HashHelper.VerifySha1(data, clientDownload.Sha1))
            {
                throw new IOException(
                    L.GetString("Str_Launch_HashMismatch"));
            }

            await File.WriteAllBytesAsync(clientJarPath, data);
        }

        private static void MergeVersionInfo(MojangVersionInfo child, MojangVersionInfo parent)
        {
            if (child.AssetIndex == null) child.AssetIndex = parent.AssetIndex;
            if (string.IsNullOrEmpty(child.Assets)) child.Assets = parent.Assets;
            if (child.Downloads == null) child.Downloads = parent.Downloads;

            if (string.IsNullOrEmpty(child.MinecraftArguments))
            {
                child.MinecraftArguments = parent.MinecraftArguments;
            }

            if (child.Arguments == null)
            {
                child.Arguments = parent.Arguments;
            }
            else if (parent.Arguments != null)
            {
                if (child.Arguments.Game == null || child.Arguments.Game.Count == 0)
                {
                    child.Arguments.Game = parent.Arguments.Game;
                }
                else if (parent.Arguments.Game != null && parent.Arguments.Game.Count > 0)
                {
                    var combinedGame = new List<object>(parent.Arguments.Game);
                    combinedGame.AddRange(child.Arguments.Game);
                    child.Arguments.Game = combinedGame;
                }

                if (child.Arguments.Jvm == null || child.Arguments.Jvm.Count == 0)
                {
                    child.Arguments.Jvm = parent.Arguments.Jvm;
                }
                else if (parent.Arguments.Jvm != null && parent.Arguments.Jvm.Count > 0)
                {
                    var combinedJvm = new List<object>(child.Arguments.Jvm);
                    combinedJvm.AddRange(parent.Arguments.Jvm);
                    child.Arguments.Jvm = combinedJvm;
                }
            }

            if (parent.Libraries != null && parent.Libraries.Count > 0)
            {
                var combinedLibs = new List<LibraryInfo>(child.Libraries);
                foreach (var parentLib in parent.Libraries)
                {
                    if (!combinedLibs.Exists(l => l.Name == parentLib.Name))
                    {
                        combinedLibs.Add(parentLib);
                    }
                }
                child.Libraries = combinedLibs;
            }
        }

        /// <summary>
        /// Возвращает содержимое первого URL, который ответил.
        ///
        /// Раньше здесь была пара «primaryUrl + один fallbackUrl», и порядок был
        /// зашит в вызывающий код. Списком кандидатов управляет MirrorService:
        /// при недоступном Mojang официальный URL уходит в конец, и запуск не
        /// тратит таймаут на заведомо мёртвый адрес.
        /// </summary>
        private static async Task<string?> FetchFirstAsync(IReadOnlyList<string> candidates, TimeSpan timeout)
        {
            foreach (string url in candidates)
            {
                try
                {
                    using var cts = new System.Threading.CancellationTokenSource(timeout);
                    return await HttpClient.GetStringAsync(url, cts.Token);
                }
                catch
                {
                    // Этот кандидат не ответил — пробуем следующий.
                }
            }

            return null;
        }

        private static async Task<byte[]?> DownloadFirstAsync(IReadOnlyList<string> candidates, TimeSpan timeout)
        {
            foreach (string url in candidates)
            {
                try
                {
                    using var cts = new System.Threading.CancellationTokenSource(timeout);
                    return await HttpClient.GetByteArrayAsync(url, cts.Token);
                }
                catch
                {
                    // Этот кандидат не ответил — пробуем следующий.
                }
            }

            return null;
        }
    }
}
