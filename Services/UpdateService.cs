using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MinecraftLauncher.Helpers;
using MinecraftLauncher.Models;

namespace MinecraftLauncher.Services
{
    public struct DownloadProgressReport
    {
        public double Percentage; // 0.0 to 1.0
        public long BytesReceived;
        public long TotalBytes;
        public double SpeedBytesPerSec;
        public TimeSpan EstimatedTimeRemaining;
        public string ActiveMirror;
    }

    public interface IUpdateService
    {
        Task<ReleaseInfo?> CheckForUpdatesAsync(bool isManual = false);
        Task<bool> DownloadUpdateWithFallbackAsync(ReleaseInfo release, string destinationPath, IProgress<DownloadProgressReport>? progress, CancellationToken ct = default);
        bool VerifySha256(string filePath, string expectedHash);
        void ApplyUpdateAndRestart(string newBinaryPath, string targetVersion);
    }

    public class UpdateService : IUpdateService
    {
        private static readonly HttpClient HttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        // Локализация для строк, которые собираются в коде: бейджи типа релиза
        // и заголовки ченджлога раньше были жёстко на русском.
        private static readonly ILocalizationService L = LocalizationService.Instance;

        public const string CurrentVersion = "v2.0.0";

        public static UpdateService Instance { get; } = new UpdateService();

        static UpdateService()
        {
            HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("QLauncher_Updater/2.0");
        }

        public async Task<ReleaseInfo?> CheckForUpdatesAsync(bool isManual = false)
        {
            try
            {
                var settings = SettingsService.Instance.Settings;
                string channel = (settings.UpdateChannel ?? "stable").ToLowerInvariant();
                string mirror = (settings.UpdateMirror ?? "auto").ToLowerInvariant();

                string apiUrl = channel == "beta"
                    ? "https://api.github.com/repos/dyagyatis/QLauncher/releases"
                    : "https://api.github.com/repos/dyagyatis/QLauncher/releases/latest";

                using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
                request.Headers.Accept.ParseAdd("application/vnd.github.v3+json");

                using var response = await HttpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                JsonElement latestRelease;
                List<JsonElement> missedReleases = new();

                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    var array = doc.RootElement.EnumerateArray().ToList();
                    if (array.Count == 0) return null;
                    latestRelease = array[0];

                    foreach (var r in array)
                    {
                        string tag = r.GetProperty("tag_name").GetString() ?? "";
                        if (IsNewerVersion(tag, CurrentVersion))
                        {
                            missedReleases.Add(r);
                        }
                    }
                }
                else
                {
                    latestRelease = doc.RootElement;
                    missedReleases.Add(latestRelease);
                }

                string tagName = latestRelease.GetProperty("tag_name").GetString() ?? "";
                string htmlUrl = latestRelease.GetProperty("html_url").GetString() ?? "";
                string rawBody = latestRelease.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
                bool isPrerelease = latestRelease.TryGetProperty("prerelease", out var pr) && pr.GetBoolean();

                DateTime? publishedAt = null;
                if (latestRelease.TryGetProperty("published_at", out var pa) && pa.TryGetDateTime(out var dt))
                {
                    publishedAt = dt;
                }

                string author = "dyagyatis";
                if (latestRelease.TryGetProperty("author", out var authElem) && authElem.TryGetProperty("login", out var logElem))
                {
                    author = logElem.GetString() ?? "dyagyatis";
                }

                bool hasUpdate = IsNewerVersion(tagName, CurrentVersion);

                if (!hasUpdate)
                {
                    return new ReleaseInfo
                    {
                        TagName = tagName,
                        HtmlUrl = htmlUrl,
                        HasUpdate = false
                    };
                }

                // Пропуск версии при фоновой проверке
                if (!isManual && !string.IsNullOrEmpty(settings.SkippedVersion) &&
                    string.Equals(settings.SkippedVersion.Trim(), tagName.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                // Поиск исполняемого файла в ассетах
                string downloadUrl = "";
                string fileName = "QLauncher.exe";
                long fileSizeBytes = 0;
                string sha256 = "";

                if (latestRelease.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        string name = asset.GetProperty("name").GetString() ?? "";
                        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                            fileSizeBytes = asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                            fileName = name;
                            break;
                        }
                    }
                }

                // Поиск SHA-256 в теле релиза (64 шестнадцатеричных символа)
                var shaMatch = Regex.Match(rawBody, @"\b[a-fA-F0-9]{64}\b");
                if (shaMatch.Success)
                {
                    sha256 = shaMatch.Value;
                }

                // Формирование ссылки на зеркало SourceForge
                string cleanTag = tagName.TrimStart('v', 'V');
                string sfUrl = $"https://downloads.sourceforge.net/project/qlauncher/v{cleanTag}/{fileName}";

                // Выбор активного зеркала
                string activeMirror = "GitHub";
                string activeDownloadUrl = downloadUrl;

                if (mirror == "sourceforge")
                {
                    activeMirror = "SourceForge";
                    activeDownloadUrl = sfUrl;
                }
                else if (string.IsNullOrEmpty(downloadUrl))
                {
                    activeMirror = "SourceForge";
                    activeDownloadUrl = sfUrl;
                }

                // Суммарный список изменений без эмодзи
                string formattedChangelog = BuildCleanChangelog(missedReleases);

                // Определение типа релиза
                string releaseType = GetReleaseTypeBadge(tagName, CurrentVersion, isPrerelease);

                return new ReleaseInfo
                {
                    TagName = tagName,
                    HtmlUrl = htmlUrl,
                    Body = rawBody,
                    HasUpdate = true,
                    DownloadUrl = downloadUrl,
                    SourceForgeUrl = sfUrl,
                    ActiveDownloadUrl = activeDownloadUrl,
                    ActiveMirror = activeMirror,
                    FileName = fileName,
                    FileSizeBytes = fileSizeBytes,
                    FileSizeFormatted = FormatBytes(fileSizeBytes),
                    Sha256 = sha256,
                    IsPrerelease = isPrerelease,
                    ReleaseDateFormatted = publishedAt?.ToString("dd MMMM yyyy") ?? DateTime.Now.ToString("dd.MM.yyyy"),
                    Author = author,
                    ReleaseTypeBadge = releaseType,
                    FormattedChangelog = formattedChangelog
                };
            }
            catch
            {
                return null;
            }
        }

        public async Task<bool> DownloadUpdateWithFallbackAsync(ReleaseInfo release, string destinationPath, IProgress<DownloadProgressReport>? progress, CancellationToken ct = default)
        {
            var settings = SettingsService.Instance.Settings;
            string mirror = (settings.UpdateMirror ?? "auto").ToLowerInvariant();

            // Попытка 1: Активное зеркало
            bool success = await DownloadInternalAsync(release.ActiveDownloadUrl, release.ActiveMirror, destinationPath, settings.BandwidthLimitMbps, progress, ct);
            if (success) return true;

            if (ct.IsCancellationRequested) return false;

            // Попытка 2: Если режим Auto и была ошибка на GitHub, пробуем SourceForge
            if (mirror == "auto" && release.ActiveMirror == "GitHub" && !string.IsNullOrEmpty(release.SourceForgeUrl))
            {
                release.ActiveMirror = "SourceForge";
                release.ActiveDownloadUrl = release.SourceForgeUrl;
                return await DownloadInternalAsync(release.SourceForgeUrl, "SourceForge", destinationPath, settings.BandwidthLimitMbps, progress, ct);
            }

            return false;
        }

        private async Task<bool> DownloadInternalAsync(string url, string mirrorName, string destinationPath, int limitMbps, IProgress<DownloadProgressReport>? progress, CancellationToken ct)
        {
            try
            {
                string? dir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                long existingBytes = 0;
                if (File.Exists(destinationPath))
                {
                    existingBytes = new FileInfo(destinationPath).Length;
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (existingBytes > 0)
                {
                    request.Headers.Range = new RangeHeaderValue(existingBytes, null);
                }

                using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

                long totalBytes = -1;
                FileMode fileMode = FileMode.Create;

                if (response.StatusCode == System.Net.HttpStatusCode.PartialContent && existingBytes > 0)
                {
                    fileMode = FileMode.Append;
                    totalBytes = existingBytes + (response.Content.Headers.ContentLength ?? 0);
                }
                else
                {
                    response.EnsureSuccessStatusCode();
                    fileMode = FileMode.Create;
                    existingBytes = 0;
                    totalBytes = response.Content.Headers.ContentLength ?? -1;
                }

                await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
                await using var fileStream = new FileStream(destinationPath, fileMode, FileAccess.Write, FileShare.None, 16384, true);

                byte[] buffer = new byte[16384];
                long totalRead = existingBytes;
                int bytesRead;

                var stopwatch = Stopwatch.StartNew();
                long bytesSinceLastTick = 0;
                double currentSpeed = 0;

                while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    totalRead += bytesRead;
                    bytesSinceLastTick += bytesRead;

                    // Ограничение скорости (Bandwidth Limiter)
                    if (limitMbps > 0)
                    {
                        long maxBytesPerSec = limitMbps * 1024L * 1024L;
                        double expectedSeconds = (double)bytesSinceLastTick / maxBytesPerSec;
                        double actualSeconds = stopwatch.Elapsed.TotalSeconds;
                        if (actualSeconds < expectedSeconds)
                        {
                            int delayMs = (int)((expectedSeconds - actualSeconds) * 1000);
                            if (delayMs > 5) await Task.Delay(delayMs, ct);
                        }
                    }

                    if (stopwatch.ElapsedMilliseconds >= 350)
                    {
                        currentSpeed = bytesSinceLastTick / stopwatch.Elapsed.TotalSeconds;
                        bytesSinceLastTick = 0;
                        stopwatch.Restart();

                        TimeSpan eta = TimeSpan.Zero;
                        if (totalBytes > 0 && currentSpeed > 0)
                        {
                            double remainingBytes = Math.Max(0, totalBytes - totalRead);
                            eta = TimeSpan.FromSeconds(remainingBytes / currentSpeed);
                        }

                        double pct = totalBytes > 0 ? (double)totalRead / totalBytes : 0;
                        progress?.Report(new DownloadProgressReport
                        {
                            Percentage = Math.Min(1.0, Math.Max(0.0, pct)),
                            BytesReceived = totalRead,
                            TotalBytes = totalBytes,
                            SpeedBytesPerSec = currentSpeed,
                            EstimatedTimeRemaining = eta,
                            ActiveMirror = mirrorName
                        });
                    }
                }

                progress?.Report(new DownloadProgressReport
                {
                    Percentage = 1.0,
                    BytesReceived = totalRead,
                    TotalBytes = totalRead,
                    SpeedBytesPerSec = currentSpeed,
                    EstimatedTimeRemaining = TimeSpan.Zero,
                    ActiveMirror = mirrorName
                });

                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool VerifySha256(string filePath, string expectedHash)
        {
            if (string.IsNullOrWhiteSpace(expectedHash) || !File.Exists(filePath)) return true;

            try
            {
                using var stream = File.OpenRead(filePath);
                byte[] hashBytes = SHA256.HashData(stream);
                string computedHash = Convert.ToHexString(hashBytes).ToLowerInvariant();
                return string.Equals(computedHash, expectedHash.Trim().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public void ApplyUpdateAndRestart(string newBinaryPath, string targetVersion)
        {
            int currentPid = Environment.ProcessId;
            string currentExe = Environment.ProcessPath ?? "";
            if (string.IsNullOrEmpty(currentExe) || !File.Exists(newBinaryPath)) return;

            string backupExe = currentExe + ".bak";

            // Сохраняем аргументы командной строки, отфильтровав старый флаг --updated
            var rawArgs = Environment.GetCommandLineArgs().Skip(1);
            var preservedArgs = new List<string>();
            bool skipNext = false;
            foreach (var arg in rawArgs)
            {
                if (skipNext) { skipNext = false; continue; }
                if (arg == "--updated") { skipNext = true; continue; }
                preservedArgs.Add(arg);
            }
            preservedArgs.Add("--updated");
            preservedArgs.Add(targetVersion);

            string escapedArgs = string.Join(" ", preservedArgs.Select(a => $"\"{a}\""));

            // PowerShell Handover скрипт
            string script = $@"
                $proc = Get-Process -Id {currentPid} -ErrorAction SilentlyContinue
                if ($proc) {{ $proc.WaitForExit(10000) }}
                Start-Sleep -Milliseconds 300

                Copy-Item -Path '{currentExe}' -Destination '{backupExe}' -Force -ErrorAction SilentlyContinue
                Copy-Item -Path '{newBinaryPath}' -Destination '{currentExe}' -Force
                
                Start-Process -FilePath '{currentExe}' -ArgumentList '{escapedArgs}'
                Remove-Item -Path '{newBinaryPath}' -Force -ErrorAction SilentlyContinue
            ";

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command \"{script}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Process.Start(psi);
            Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
        }

        private static bool IsNewerVersion(string latestTag, string currentTag)
        {
            string cleanLatest = latestTag.TrimStart('v', 'V').Trim();
            string cleanCurrent = currentTag.TrimStart('v', 'V').Trim();

            if (Version.TryParse(cleanLatest, out var latest) && Version.TryParse(cleanCurrent, out var current))
            {
                return latest > current;
            }

            return !string.Equals(latestTag.Trim(), currentTag.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static string GetReleaseTypeBadge(string newTag, string currentTag, bool isPrerelease)
        {
            if (isPrerelease) return L.GetString("Str_Release_Prerelease");

            string cleanLatest = newTag.TrimStart('v', 'V').Trim();
            string cleanCurrent = currentTag.TrimStart('v', 'V').Trim();

            if (Version.TryParse(cleanLatest, out var latest) && Version.TryParse(cleanCurrent, out var current))
            {
                if (latest.Major > current.Major) return L.GetString("Str_Release_Major");
                if (latest.Minor > current.Minor) return L.GetString("Str_Release_Features");
                return L.GetString("Str_Release_Fix");
            }

            return L.GetString("Str_Release_Generic");
        }

        private static string BuildCleanChangelog(List<JsonElement> releases)
        {
            if (releases.Count == 0) return L.GetString("Str_Release_ChangelogEmpty");

            var sb = new StringBuilder();

            foreach (var r in releases)
            {
                string tag = r.GetProperty("tag_name").GetString() ?? "";
                string body = r.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";

                if (releases.Count > 1)
                {
                    sb.AppendLine(L.Format("Str_Release_ChangelogTag", tag));
                }

                // Очистка от эмодзи и форматирование списков
                string cleaned = CleanEmojis(body);
                var lines = cleaned.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    string trimmed = line.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed)) continue;

                    // Форматируем markdown-заголовки
                    if (trimmed.StartsWith("###") || trimmed.StartsWith("##") || trimmed.StartsWith("#"))
                    {
                        string headerText = trimmed.TrimStart('#').Trim();
                        sb.AppendLine($"\n[{headerText}]");
                    }
                    else if (trimmed.StartsWith("-") || trimmed.StartsWith("*"))
                    {
                        sb.AppendLine($"  • {trimmed.TrimStart('-', '*', ' ')}");
                    }
                    else
                    {
                        sb.AppendLine($"  {trimmed}");
                    }
                }

                sb.AppendLine();
            }

            return sb.ToString().Trim();
        }

        private static string CleanEmojis(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            // Удаляем суррогатные пары и графические эмодзи
            return Regex.Replace(text, @"\p{Cs}|\p{So}|\p{Cn}", "");
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return L.GetString("Str_Update_SizeUnknown");
            double mb = bytes / (1024.0 * 1024.0);
            return L.Format("Str_Size_Mb", $"{mb:F1}");
        }
    }
}

