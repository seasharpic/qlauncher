using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MinecraftLauncher.Helpers;
using MinecraftLauncher.Models;
using MinecraftLauncher.Services.LaunchEngine.Models;

namespace MinecraftLauncher.Services.LaunchEngine
{
    public class MrPackInstaller
    {
        private static readonly HttpClient HttpClient = new(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            MaxConnectionsPerServer = 16
        });

        public static async Task<ModpackProfile> InstallMrPackAsync(
            string mrPackFilePath,
            string gameRootPath,
            IProgress<LaunchProgress>? progress = null)
        {
            if (!File.Exists(mrPackFilePath))
                throw new FileNotFoundException(LocalizationService.Instance.GetString("Str_MrPack_NotFound"), mrPackFilePath);

            using var archive = ZipFile.OpenRead(mrPackFilePath);
            var indexEntry = archive.GetEntry("modrinth.index.json");
            if (indexEntry == null)
                throw new InvalidDataException(LocalizationService.Instance.GetString("Str_MrPack_IndexMissing"));

            using var indexStream = indexEntry.Open();
            using var doc = await JsonDocument.ParseAsync(indexStream);
            var root = doc.RootElement;

            string packName = root.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(packName))
                packName = Path.GetFileNameWithoutExtension(mrPackFilePath);

            string mcVersion = "1.20.1";
            string loader = "Fabric";

            if (root.TryGetProperty("dependencies", out var depsEl))
            {
                if (depsEl.TryGetProperty("minecraft", out var mcEl))
                    mcVersion = mcEl.GetString() ?? mcVersion;

                if (depsEl.TryGetProperty("fabric-loader", out _))
                    loader = "Fabric";
                else if (depsEl.TryGetProperty("quilt-loader", out _))
                    loader = "Quilt";
                else if (depsEl.TryGetProperty("forge", out _) || depsEl.TryGetProperty("neoforge", out _))
                    loader = "Forge";
            }

            string instanceDir = Path.Combine(gameRootPath, "instances", packName);
            if (Directory.Exists(instanceDir))
            {
                instanceDir += "_" + DateTime.Now.ToString("HHmmss");
            }
            Directory.CreateDirectory(instanceDir);

            // Элементы с небезопасными путями: молча пропускаем, но сообщаем пользователю,
            // чтобы он понимал, почему сборка импортировалась не полностью.
            var skippedEntries = new System.Collections.Generic.List<string>();

            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.StartsWith("overrides/", StringComparison.OrdinalIgnoreCase))
                {
                    string relPath = entry.FullName.Substring(10);
                    if (string.IsNullOrEmpty(relPath) || relPath.EndsWith("/")) continue;

                    // Имя zip-записи берём из недоверенного архива: пропускаем элементы,
                    // уводящие за пределы папки экземпляра.
                    string? destPath = SafePath.TryCombineWithin(instanceDir, relPath);
                    if (destPath == null)
                    {
                        skippedEntries.Add($"overrides/{relPath}");
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                    entry.ExtractToFile(destPath, true);
                }
            }

            if (root.TryGetProperty("files", out var filesEl))
            {
                var filesToDownload = new System.Collections.Generic.List<(string url, string destPath)>();
                foreach (var fileEl in filesEl.EnumerateArray())
                {
                    string path = fileEl.GetProperty("path").GetString() ?? "";
                    if (fileEl.TryGetProperty("downloads", out var downloadsEl) && downloadsEl.GetArrayLength() > 0)
                    {
                        string downloadUrl = downloadsEl[0].GetString() ?? "";
                        if (!string.IsNullOrEmpty(downloadUrl) && !string.IsNullOrEmpty(path))
                        {
                            // path приходит из modrinth.index.json внутри архива и не проверяется .NET,
                        // поэтому обычный Path.Combine позволял записать файл
                        // в любую точку диска ("C:/Windows/...").
                        string? destPath = SafePath.TryCombineWithin(instanceDir, path);
                        if (destPath == null)
                        {
                            skippedEntries.Add(path);
                            continue;
                        }

                        filesToDownload.Add((downloadUrl, destPath));
                        }
                    }
                }

                int total = filesToDownload.Count;
                int completed = 0;
                using var semaphore = new SemaphoreSlim(8);
                var tasks = new System.Collections.Generic.List<Task>();

                foreach (var item in filesToDownload)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        await semaphore.WaitAsync();
                        try
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(item.destPath)!);
                            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                            byte[] data = await HttpClient.GetByteArrayAsync(item.url, cts.Token);
                            await File.WriteAllBytesAsync(item.destPath, data);

                            int cur = Interlocked.Increment(ref completed);
                            progress?.Report(new LaunchProgress
                            {
                                Phase = LaunchPhase.DownloadingAssets,
                                StatusText = LocalizationService.Instance.Format("Str_MrPack_Importing", cur, total),
                                Percentage = (int)((cur / (double)total) * 100)
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

            if (skippedEntries.Count > 0)
            {
                progress?.Report(new LaunchProgress
                {
                    Phase = LaunchPhase.Completed,
                    StatusText = LocalizationService.Instance.Format("Str_MrPack_Skipped", skippedEntries.Count),
                    Percentage = 100
                });
            }

            return new ModpackProfile
            {
                Name = packName,
                GameVersion = mcVersion,
                Loader = loader,
                FolderPath = instanceDir
            };
        }
    }
}
