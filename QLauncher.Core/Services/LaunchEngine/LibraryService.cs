using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MinecraftLauncher.Helpers;
using MinecraftLauncher.Services.LaunchEngine.Models;

namespace MinecraftLauncher.Services.LaunchEngine
{
    public class LibraryService
    {
        private static readonly HttpClient HttpClient = new(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            MaxConnectionsPerServer = 32,
            AllowAutoRedirect = true
        });

        /// <summary>
        /// Переставляет кандидатов так, чтобы недоступные хосты оказались в конце.
        ///
        /// Идея из PineconeMC (PineconeNetworkCheck): если maven.fabricmc.net
        /// недоступен из сети пользователя, каждый библиотечный файл начинался
        /// с попытки достучаться до него и тратил таймаут. Здесь мы сортируем
        /// список по результату проверки хоста, сохраняя взаимный порядок
        /// доступных кандидатов, чтобы официальный источник оставался первым
        /// по умолчанию.
        ///
        /// При включённом OfficialOnly список не переставляется вовсе.
        /// </summary>
        private static List<string> OrderMavenCandidates(List<string> candidates, string relativePath)
        {
            if (candidates.Count < 2 || MirrorService.Instance.Preference == MirrorMode.OfficialOnly)
            {
                return candidates;
            }

            // Официальные кандидаты по умолчанию: адрес из манифеста плюс
            // репозиторий Mojang.
            var official = new List<string>(candidates.Count);
            string? mirror = null;

            foreach (string candidate in candidates)
            {
                if (MirrorService.GetHost(candidate) == MirrorService.BmclapiHost)
                {
                    mirror = candidate;
                }
                else
                {
                    official.Add(candidate);
                }
            }

            if (mirror == null)
            {
                return candidates;
            }

            bool officialReachable = official.Exists(url => MirrorService.IsHostReachable(MirrorService.GetHost(url)));
            bool mirrorReachable = MirrorService.IsHostReachable(MirrorService.BmclapiHost);

            if (mirrorReachable && !officialReachable)
            {
                var reordered = new List<string>(official.Count + 1) { mirror };
                reordered.AddRange(official);
                return reordered;
            }

            return candidates;
        }

        public async Task<List<string>> EnsureLibrariesAsync(
            string gameRootPath,
            MojangVersionInfo versionInfo,
            string nativesExtractDir,
            IProgress<LaunchProgress>? progress = null)
        {
            var classpathJars = new List<string>();
            string librariesRoot = Path.Combine(gameRootPath, "libraries");
            Directory.CreateDirectory(librariesRoot);
            Directory.CreateDirectory(nativesExtractDir);

            // Ожидаемый SHA-1 из манифеста Mojang: null, если манифест его не содержит.
            var downloadTasks = new List<(List<string> candidateUrls, string localPath, bool isNative, string? expectedSha1)>();

            foreach (var lib in versionInfo.Libraries)
            {
                if (!IsRuleAllowed(lib.Rules)) continue;

                if (lib.Downloads?.Artifact != null)
                {
                    string localRel = GetArtifactRelativePath(lib.Name, lib.Downloads.Artifact.Url);
                    string localPath = Path.Combine(librariesRoot, localRel);
                    classpathJars.Add(localPath);

                    if (!File.Exists(localPath) || new FileInfo(localPath).Length == 0)
                    {
                        string primaryUrl = lib.Downloads.Artifact.Url;
                        string cleanRel = localRel.Replace('\\', '/');
                        var candidates = new List<string>
                        {
                            primaryUrl,
                            $"https://repo1.maven.org/maven2/{cleanRel}",
                            $"https://bmclapi2.bangbang93.com/maven/{cleanRel}"
                        };
                        candidates = OrderMavenCandidates(candidates, cleanRel);
                        downloadTasks.Add((candidates, localPath, false, lib.Downloads.Artifact.Sha1));
                    }
                }
                else if (!string.IsNullOrEmpty(lib.Name))
                {
                    string localRel = CoordinateToRelativePath(lib.Name);
                    string localPath = Path.Combine(librariesRoot, localRel);
                    classpathJars.Add(localPath);

                    if (!File.Exists(localPath) || new FileInfo(localPath).Length == 0)
                    {
                        string cleanRel = localRel.Replace('\\', '/');
                        string baseUrl = string.IsNullOrEmpty(lib.Url) ? "https://libraries.minecraft.net/" : lib.Url;
                        if (!baseUrl.EndsWith("/")) baseUrl += "/";

                        string primaryUrl = baseUrl + cleanRel;
                        var candidates = new List<string> { primaryUrl };

                        if (!primaryUrl.Contains("maven.fabricmc.net"))
                        {
                            candidates.Add($"https://maven.fabricmc.net/{cleanRel}");
                        }
                        candidates.Add($"https://repo1.maven.org/maven2/{cleanRel}");
                        candidates.Add($"https://bmclapi2.bangbang93.com/maven/{cleanRel}");

                        // У этой ветки манифест не даёт хеша (артефакт строится из координаты),
                            // поэтому сверять нечего.
                        downloadTasks.Add((OrderMavenCandidates(candidates, cleanRel), localPath, false, null));
                    }
                }

                if (lib.Natives != null && lib.Natives.TryGetValue("windows", out var classifierKey) && lib.Downloads?.Classifiers != null)
                {
                    classifierKey = classifierKey.Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32");
                    if (lib.Downloads.Classifiers.TryGetValue(classifierKey, out var nativeArtifact))
                    {
                        string nativeRel = GetArtifactRelativePath(lib.Name + "-" + classifierKey, nativeArtifact.Url);
                        string nativePath = Path.Combine(librariesRoot, nativeRel);

                        if (!File.Exists(nativePath) || new FileInfo(nativePath).Length == 0)
                        {
                            string cleanRel = nativeRel.Replace('\\', '/');
                            var candidates = OrderMavenCandidates(
                                new List<string>
                                {
                                    nativeArtifact.Url,
                                    $"https://bmclapi2.bangbang93.com/maven/{cleanRel}"
                                },
                                cleanRel);
                            downloadTasks.Add((candidates, nativePath, true, nativeArtifact.Sha1));
                        }
                        else
                        {
                            ExtractNativeJar(nativePath, nativesExtractDir);
                        }
                    }
                }
            }

            int total = downloadTasks.Count;
            if (total > 0)
            {
                int completed = 0;
                using var semaphore = new SemaphoreSlim(16);

                var tasks = new List<Task>();
                foreach (var item in downloadTasks)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        await semaphore.WaitAsync();
                        try
                        {
                            await DownloadFileWithFallbackAsync(item.candidateUrls, item.localPath, item.expectedSha1);
                            if (item.isNative && File.Exists(item.localPath))
                            {
                                ExtractNativeJar(item.localPath, nativesExtractDir);
                            }

                            int cur = Interlocked.Increment(ref completed);
                            int pct = 20 + (int)((cur / (double)total) * 40);
                            progress?.Report(new LaunchProgress
                            {
                                Phase = LaunchPhase.DownloadingLibraries,
                                StatusText = LocalizationService.Instance.Format("Str_Launch_DownloadLibraries", cur, total),
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

            string clientJar = Path.Combine(gameRootPath, "versions", versionInfo.Id, $"{versionInfo.Id}.jar");
            if (File.Exists(clientJar) && !classpathJars.Contains(clientJar))
            {
                classpathJars.Add(clientJar);
            }
            else if (!string.IsNullOrEmpty(versionInfo.InheritsFrom))
            {
                string parentClientJar = Path.Combine(gameRootPath, "versions", versionInfo.InheritsFrom, $"{versionInfo.InheritsFrom}.jar");
                if (File.Exists(parentClientJar) && !classpathJars.Contains(parentClientJar))
                {
                    classpathJars.Add(parentClientJar);
                }
            }

            var validClasspath = new List<string>();
            var missingLibraries = new List<string>();

            foreach (var jar in classpathJars)
            {
                if (File.Exists(jar) && new FileInfo(jar).Length > 0)
                {
                    if (!validClasspath.Contains(jar))
                    {
                        validClasspath.Add(jar);
                    }
                }
                else
                {
                    missingLibraries.Add(Path.GetFileName(jar));
                }
            }

            if (missingLibraries.Count > 0)
            {
                bool criticalMissing = missingLibraries.Exists(m =>
                    m.Contains("asm", StringComparison.OrdinalIgnoreCase) ||
                    m.Contains("fabric-loader", StringComparison.OrdinalIgnoreCase) ||
                    m.Contains("sponge-mixin", StringComparison.OrdinalIgnoreCase));

                if (criticalMissing)
                {
                    throw new InvalidOperationException(LocalizationService.Instance.Format(
                    "Str_Library_CriticalFailed",
                    string.Join(", ", missingLibraries.GetRange(0, Math.Min(3, missingLibraries.Count)))));
                }
            }

            return validClasspath;
        }

        private static bool IsRuleAllowed(List<RuleInfo>? rules)
        {
            if (rules == null || rules.Count == 0) return true;

            bool allowed = false;
            foreach (var rule in rules)
            {
                if (rule.Action == "allow")
                {
                    if (rule.Os == null || rule.Os.Name == "windows")
                    {
                        allowed = true;
                    }
                }
                else if (rule.Action == "disallow")
                {
                    if (rule.Os != null && rule.Os.Name == "windows")
                    {
                        allowed = false;
                    }
                }
            }
            return allowed;
        }

        private static string CoordinateToRelativePath(string coordinate)
        {
            var parts = coordinate.Split(':');
            if (parts.Length < 3) return coordinate;

            string group = parts[0].Replace('.', Path.DirectorySeparatorChar);
            string artifact = parts[1];
            string version = parts[2];
            string classifier = parts.Length > 3 ? $"-{parts[3]}" : "";

            return Path.Combine(group, artifact, version, $"{artifact}-{version}{classifier}.jar");
        }

        private static string GetArtifactRelativePath(string name, string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                string path = uri.AbsolutePath.TrimStart('/');
                if (path.StartsWith("maven/")) path = path.Substring(6);
                return path.Replace('/', Path.DirectorySeparatorChar);
            }
            return CoordinateToRelativePath(name);
        }

        private static void ExtractNativeJar(string jarPath, string extractTo)
        {
            try
            {
                using var zip = ZipFile.OpenRead(jarPath);
                foreach (var entry in zip.Entries)
                {
                    if (entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && !entry.FullName.Contains('/'))
                    {
                        string dest = Path.Combine(extractTo, entry.Name);
                        entry.ExtractToFile(dest, true);
                    }
                }
            }
            catch { }
        }

        private static async Task DownloadFileWithFallbackAsync(
            IEnumerable<string> candidateUrls,
            string destinationPath,
            string? expectedSha1 = null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

            foreach (var url in candidateUrls)
            {
                if (string.IsNullOrWhiteSpace(url)) continue;

                try
                {
                    // Таймаут покрывает и подключение, и чтение тела: библиотеки
                    // весят десятки мегабайт, и 10 секунд на всё обрывали загрузку.
                    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                    var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                    if (response.IsSuccessStatusCode)
                    {
                        // Чанковое чтение с лимитом скорости: библиотеки — это
                        // основной объём загрузки при первом запуске версии.
                        await using var responseStream = await response.Content.ReadAsStreamAsync(cts.Token);
                        using var buffer = new MemoryStream();
                        byte[] chunk = new byte[81920];

                        int read;
                        while ((read = await responseStream.ReadAsync(chunk, cts.Token)) > 0)
                        {
                            await buffer.WriteAsync(chunk.AsMemory(0, read), cts.Token);
                            await BandwidthLimiter.ThrottleAsync(read, cts.Token);
                        }

                        byte[] data = buffer.ToArray();
                        if (data.Length == 0) continue;

                        // JAR попадает в classpath и исполняется, поэтому сверяем хеш
                        // из манифеста Mojang. Раньше Sha1 в модели объявлялся, но не
                        // проверялся никогда — подмена файла проходила незамеченной.
                        if (!string.IsNullOrEmpty(expectedSha1) && !HashHelper.VerifySha1(data, expectedSha1))
                        {
                            continue;
                        }

                        await File.WriteAllBytesAsync(destinationPath, data);
                        return;
                    }
                }
                catch { }
            }
        }
    }
}
